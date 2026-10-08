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

    ★ -Toggle(默认关闭,CI 里也不开):
      上面那轮用的是 --status,它只走到"读状态"为止 —— 也就是说
      Start/StopTetheringAsync 这两个真正干活的调用**从来没被真机验证过**,
      它们靠的只是"和读状态走同一套反射机制"这个推论。
      加上 -Toggle 就会真开一次、再关一次,把这最后一环也走一遍。

      之所以默认不开:开热点会真改机器状态(本机、以及接到这台机器上的设备),
      而且 CI 的 runner 未必支持移动热点 —— 那种情况下退出码是 4,
      分不清"代码坏了"还是"这台机器开不了",不适合当门槛。
      想手动验一次就:  pwsh scripts/smoke-test.ps1 -Toggle

    用法:pwsh scripts/smoke-test.ps1 [-ExeDir <构建输出目录>] [-Toggle]
#>
param(
    [string]$ExeDir = 'src/HotspotGo/bin/Release/net48',
    [switch]$Toggle
)

$ErrorActionPreference = 'Stop'

$exe = Join-Path $ExeDir 'HotspotGo.exe'
$log = Join-Path $ExeDir 'log.txt'

if (-not (Test-Path $exe)) { throw "找不到构建产物:$exe" }

# 跑一次 exe,只取本次新追加的那段日志(历史日志是线索,不能删)
function Invoke-HotspotGo {
    param([string[]]$Arguments)

    $lengthBefore = 0
    if (Test-Path $log) { $lengthBefore = (Get-Item $log).Length }

    $process = Start-Process -FilePath $exe -ArgumentList $Arguments -Wait -PassThru

    $text = ''
    if (Test-Path $log) {
        $bytes = [System.IO.File]::ReadAllBytes($log)
        if ($bytes.Length -gt $lengthBefore) {
            $text = [System.Text.Encoding]::UTF8.GetString($bytes, $lengthBefore, $bytes.Length - $lengthBefore)
        }
    }

    return [pscustomobject]@{ Code = $process.ExitCode; Log = $text }
}

# 日志是唯一的排障入口,所以"这次运行留下了完整的一首一尾"必须成立。
# 判定只看 ASCII 标记,不看中文文案(见文件头的编码约定)。
function Assert-CompleteLog {
    param([string]$Text, [string]$Label)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        throw "冒烟失败:$Label 没有写出 log.txt —— 它是唯一的排障入口"
    }

    # 启动 / 结束两行都用 "=====" 包着,一条完整日志里至少出现两次
    if (([regex]::Matches($Text, '=====')).Count -lt 2) {
        throw "冒烟失败:$Label 的 log.txt 不完整(缺少启动 / 结束标记)"
    }

    # 每行都该带 yyyy-MM-dd HH:mm:ss 时间戳
    if ($Text -notmatch '(?m)^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}  ') {
        throw "冒烟失败:$Label 的 log.txt 行缺少时间戳前缀"
    }
}

# ===================================================================
# 主流程:--status(只读,不改机器状态)
# ===================================================================

# --wait 5 只给"等上游连接就绪"设个上限,免得这台机器没外网时白等默认的 90 秒
$statusArgs = @('--wait', '5', '--status')
$status = Invoke-HotspotGo -Arguments $statusArgs
$code = $status.Code
$text = $status.Log

Write-Host "退出码:$code"
Write-Host '----------------------- log.txt(本次新增的部分) -----------------------'
if ($text) { Write-Host $text } else { Write-Host '(没有写出 log.txt)' }
Write-Host '------------------------------------------------------------------------'

if ($code -notin 0, 2, 3) { throw "冒烟失败:退出码 $code(1 = 顶层未处理异常,4 = 开启热点失败)" }

Assert-CompleteLog -Text $text -Label '--status'

# 启动行会原样回显参数。这里从 $statusArgs 反推期望值,而不是硬编码字符串 ——
# 以后改了上面的参数,这条断言会自动跟着变。
$expectedEcho = 'args: ' + ($statusArgs -join ' ')
if ($text -notmatch [regex]::Escape($expectedEcho)) {
    throw "冒烟失败:启动日志没有回显本次传入的参数($expectedEcho)"
}

# 退出码 0 说明真的拿到了管理器并读到了状态,那 SSID 这一行必须在
if ($code -eq 0 -and $text -notmatch 'SSID:') {
    throw '冒烟失败:退出码为 0,却读不到 SSID 行'
}

# ===================================================================
# -Toggle:真开一次、再关一次(默认不开,理由见文件头)
# ===================================================================

if ($Toggle) {
    Write-Host ''
    Write-Host '=== -Toggle:真实开热点一次,再关掉 ==='

    $on = Invoke-HotspotGo -Arguments @('--wait', '5')
    Write-Host "开启退出码:$($on.Code)"
    Write-Host '--- log.txt(开启) ---'
    if ($on.Log) { Write-Host $on.Log } else { Write-Host '(没有写出 log.txt)' }

    # 唯独 1 是"代码坏了"(未处理异常逃到顶层),其余都要分开看
    if ($on.Code -eq 1) { throw '冒烟失败:-Toggle 开启时顶层抛出未处理异常(退出码 1)' }

    Assert-CompleteLog -Text $on.Log -Label '-Toggle 开启'

    if ($on.Code -eq 0) {
        Write-Host '  → 热点已开:StartTetheringAsync 这条真机链路走通了'
    } elseif ($on.Code -eq 4) {
        Write-Host '  → 这台机器开不了热点(退出码 4):多半是网卡 / 驱动不支持移动热点。'
        Write-Host '    不算冒烟失败,但这台机器验证不了开启链路 —— 看上面日志里的 [错误] 行确认原因。'
    } else {
        Write-Host "  → 环境所限(退出码 $($on.Code)):没有可共享的连接或拿不到热点管理器。"
    }

    # 无论上面结果如何都关一次:开不开得成是环境问题,但"关"必须永远是安全的
    $off = Invoke-HotspotGo -Arguments @('--off')
    Write-Host "关闭退出码:$($off.Code)"
    Write-Host '--- log.txt(关闭) ---'
    if ($off.Log) { Write-Host $off.Log } else { Write-Host '(没有写出 log.txt)' }

    Assert-CompleteLog -Text $off.Log -Label '-Toggle 关闭'
    if ($off.Code -ne 0) { throw "冒烟失败:-Toggle 关闭阶段退出码 $($off.Code)(期望 0)" }
}

Write-Host '冒烟通过'
