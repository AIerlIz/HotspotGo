<#
    冒烟测试:真实运行一遍构建产物,确认它在一台"干净"的机器上能跑起来。

    为什么单元测试之外还需要这个:
      单元测试只覆盖不依赖系统状态的那部分:参数解析 / 反射通道 / 结果对象 / 日志器 /
      退出码契约,以及 Core 的编排判断(靠假实现替掉 WinRT 操作来测)。
      真正调 WinRT 开热点的那部分依赖系统状态(有没有可共享的网卡、icssvc 起没起),
      没法写单元测试。这里直接跑 exe,验证三件单测覆盖不到的事:
        1. net48 运行时能加载这个程序集;
        2. WinRT 类型解析 + 反射调用这条链路能通到底;
        3. 没有任何未处理异常逃到顶层 —— 逃出去的话退出码会是 1。

    判定规则(退出码):
      0 = 正常读到热点状态;2 = 这台机器没有可用连接;3 = 拿不到热点管理器。
      后两种是环境所限(CI 的 runner 未必有可共享的网卡),不算代码问题;
      1(顶层未处理异常)和 4(开启了却失败)才是真出问题。

    两条实现约定:
      - 不删历史 log.txt,只读取本次运行新追加的那一段 —— 那是排查问题的线索,不该毁掉;
      - 所有"通过 / 失败"的判定都用 ASCII 匹配,不依赖中文日志文本,这样即使在按
        ANSI 读文件的旧版 PowerShell 下跑,判定结果也不受编码影响。

    用法:pwsh scripts/smoke-test.ps1 [-ExeDir <构建输出目录>]
#>
param(
    [string]$ExeDir = 'src/HotspotGo/bin/Release/net48'
)

$ErrorActionPreference = 'Stop'

$exe = Join-Path $ExeDir 'HotspotGo.exe'
$log = Join-Path $ExeDir 'log.txt'

if (-not (Test-Path $exe)) { throw "找不到构建产物:$exe" }

# 记下运行前的长度,稍后只看新增的那一段
$lengthBefore = 0
if (Test-Path $log) { $lengthBefore = (Get-Item $log).Length }

# --wait 5 只给"等上游连接就绪"设个上限,免得这台机器没外网时白等默认的 90 秒
$exeArgs = @('--wait', '5', '--status')
$process = Start-Process -FilePath $exe -ArgumentList $exeArgs -Wait -PassThru
$code = $process.ExitCode

$text = ''
if (Test-Path $log) {
    $bytes = [System.IO.File]::ReadAllBytes($log)
    if ($bytes.Length -gt $lengthBefore) {
        $text = [System.Text.Encoding]::UTF8.GetString($bytes, $lengthBefore, $bytes.Length - $lengthBefore)
    }
}

Write-Host "退出码:$code"
Write-Host '----------------------- log.txt(本次新增的部分) -----------------------'
if ($text) { Write-Host $text } else { Write-Host '(没有写出 log.txt)' }
Write-Host '------------------------------------------------------------------------'

if ($code -notin 0, 2, 3) { throw "冒烟失败:退出码 $code(1 = 顶层未处理异常,4 = 开启热点失败)" }

if ([string]::IsNullOrWhiteSpace($text)) { throw '冒烟失败:没有写出 log.txt —— 它是唯一的排障入口' }

# 启动 / 结束两行都用 "=====" 包着,一条完整日志里至少出现两次
if (([regex]::Matches($text, '=====')).Count -lt 2) {
    throw '冒烟失败:log.txt 不完整(缺少启动 / 结束标记)'
}

# 启动行会原样回显参数。这里从 $exeArgs 反推期望值,而不是硬编码字符串 ——
# 以后改了上面的参数,这条断言会自动跟着变。
$expectedEcho = 'args: ' + ($exeArgs -join ' ')
if ($text -notmatch [regex]::Escape($expectedEcho)) {
    throw "冒烟失败:启动日志没有回显本次传入的参数($expectedEcho)"
}

# 每行都该带 yyyy-MM-dd HH:mm:ss 时间戳
if ($text -notmatch '(?m)^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}  ') {
    throw '冒烟失败:log.txt 的行缺少时间戳前缀'
}

# 退出码 0 说明真的拿到了管理器并读到了状态,那 SSID 这一行必须在
if ($code -eq 0 -and $text -notmatch 'SSID:') {
    throw '冒烟失败:退出码为 0,却读不到 SSID 行'
}

Write-Host '冒烟通过'
