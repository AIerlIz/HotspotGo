# 热点快启 · HotspotGo

一键打开 Windows 移动热点。

## 用法

| 命令                        | 效果                                |
| ------------------------- | --------------------------------- |
| `HotspotGo.exe`           | 等电脑连上网，然后开启热点（默认最多等 90 秒）         |
| `HotspotGo.exe --wait 30` | 同上，最多等 30 秒                       |
| `HotspotGo.exe --any`     | 不等外网，直接用当前可用连接开启热点（电脑没网时也能开）      |
| `HotspotGo.exe --status`  | 查看状态（开关 / Wi-Fi 名 / 已连设备数），不做任何改动 |
| `HotspotGo.exe --off`     | 关闭热点                              |

- 日志写在 exe 同目录的 `log.txt`
- 开机自启：把 `HotspotGo.exe` 的快捷方式放进启动文件夹（`Win+R` 输入 `shell:startup`）
