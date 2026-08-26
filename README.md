# FileCryptoTool

文件加密 / 解密工具（WinUI 3）。基于 AES 对称加密，支持拖入文件、实时进度显示，采用 TRAE 风格深色界面（纯色无渐变）。

## 功能特性

- **加密 / 解密**：AES 加密算法，每次加密自动生成随机 Salt 与 IV，相同密码每次输出不同密文
- **拖入文件**：将文件直接拖入窗口即可快速指定输入文件，自动生成输出路径
- **文件选择**：支持「浏览文件…」与「保存到…」选择输入输出
- **进度显示**：加密/解密过程实时显示进度条与百分比
- **文件头校验**：自定义 `FCT1` 魔数与版本号，解密前自动校验文件是否为本工具生成，避免误用文件
- **命令行模式**：支持无界面批量加密/解密
- **自包含发布**：打包 .NET 8 运行时 + Windows App Runtime，目标机器免安装运行库

## 使用方法

### 图形界面

1. 运行 `FileCryptoTool.exe`
2. 将文件拖入窗口（或点击「浏览文件…」）
3. 确认输出路径（默认自动生成）
4. 输入加密 / 解密密码
5. 点击「加密」或「解密」
6. 查看进度条与状态栏反馈

### 命令行

```text
FileCryptoTool <encrypt|decrypt> <输入文件> <输出文件> <密码>
```

```text
FileCryptoTool encrypt plain.txt plain.txt.enc 20310710
FileCryptoTool decrypt plain.txt.enc plain.txt 20310710
```

退出码：`0` 成功，`1` 参数错误，`2` 操作失败（密码错误 / 文件损坏 / 非本工具文件）。

## 加密文件格式

```
偏移 0      : 4 字节魔数 "FCT1"
偏移 4      : 1 字节版本号
偏移 5      : 16 字节随机 Salt
偏移 21     : 16 字节随机 IV
偏移 37     : AES 密文
```

密钥由密码 + Salt 经 PBKDF2（默认 100,000 次迭代）派生。**密码丢失无法找回，请妥善保管。**

## 构建发布

环境要求：.NET 8 SDK、Windows 10 1809（17763）及以上。

```text
dotnet build -c Release

dotnet publish -c Release -r win-x64 --self-contained
```

发布产物位于 `bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\`，可整目录复制到其他 Win10/Win11 x64 机器直接运行。

## 技术栈

- C# / .NET 8
- WinUI 3（Windows App SDK 1.6）
- AES-256-CBC + PBKDF2

## 许可证与免责声明

本项目基于 [MIT 许可证](LICENSE) 开源。使用本项目前请务必阅读 [免责声明](DISCLAIMER.md)，你应对加密文件密码的安全及用途的合规性自行负责。
