using Microsoft.UI.Xaml;

namespace FileCryptoTool;

/// <summary>
/// 应用程序入口类。
/// 负责初始化 WinUI 3 应用资源并在启动时创建主窗口。
/// </summary>
public partial class App : Application
{
    // 主窗口引用，防止启动后被垃圾回收
    private Window? _mainWindow;

    /// <summary>
    /// 初始化 App 实例并加载 XAML 资源。
    /// 若检测到命令行参数，则进入命令行模式并直接退出，不启动 GUI。
    /// </summary>
    public App()
    {
        string[] commandLineArgs = Environment.GetCommandLineArgs();

        // 命令行参数数量大于 1 时，进入无界面模式
        if (commandLineArgs.Length > 1)
        {
            int exitCode = CommandLineRunner.Run(commandLineArgs);
            Environment.Exit(exitCode);
        }

        // 调用 XAML 生成的 InitializeComponent，合并 ResourceDictionary
        this.InitializeComponent();
    }

    /// <summary>
    /// 应用启动时触发。
    /// </summary>
    /// <param name="args">启动参数。</param>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 创建并激活主窗口
        _mainWindow = new MainWindow();
        _mainWindow.Activate();
    }
}
