using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.IO;
using System.Security.Cryptography;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FileCryptoTool;

/// <summary>
/// 主窗口。
/// 提供文件选择、输出路径设置、密码输入、加解密执行与进度展示功能。
/// </summary>
public sealed partial class MainWindow : Window
{
    // 当前选中的输入文件路径
    private string? _inputFilePath;

    // 当前选中的输出文件路径
    private string? _outputFilePath;

    /// <summary>
    /// 初始化主窗口。
    /// </summary>
    public MainWindow()
    {
        this.InitializeComponent();

        // 设置窗口标题与初始尺寸
        this.Title = "FileCryptoTool // 文件加密/解密工具";
        this.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(560, 360, 680, 600));
    }

    /// <summary>
    /// 点击"浏览文件"按钮：打开文件选择器并设置输入文件。
    /// </summary>
    private async void OnSelectInputClick(object sender, RoutedEventArgs e)
    {
        FileOpenPicker picker = new();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");

        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        SetInputFile(file.Path);
    }

    /// <summary>
    /// 拖拽悬停时触发：仅当拖拽内容是文件时才允许放置。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">拖拽事件参数。</param>
    private void OnDragOver(object sender, DragEventArgs e)
    {
        // 仅接受文件拖入；非文件拖入时显示禁止光标
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "释放以选择该文件";
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    /// <summary>
    /// 拖放落下时触发：读取第一个拖入文件并设置为输入文件。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">拖拽事件参数。</param>
    private async void OnDrop(object sender, DragEventArgs e)
    {
        // 拖入内容必须包含文件
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        IReadOnlyList<IStorageItem>? items = await e.DataView.GetStorageItemsAsync();
        if (items is null || items.Count == 0 || items[0] is not StorageFile file)
        {
            return;
        }

        SetInputFile(file.Path);
    }

    /// <summary>
    /// 设置输入文件路径，并自动推荐输出路径、刷新界面状态。
    /// </summary>
    /// <param name="path">输入文件完整路径。</param>
    private void SetInputFile(string path)
    {
        _inputFilePath = path;
        InputPathTextBox.Text = _inputFilePath;

        // 根据输入文件自动推荐输出路径
        SuggestOutputPath(_inputFilePath);

        UpdateStatus("文件已选择，请确认输出路径并输入密码。", StatusType.Info);
        UpdateOperationButtons();
    }

    /// <summary>
    /// 点击"保存到..."按钮：打开保存选择器并设置输出文件。
    /// </summary>
    private async void OnSelectOutputClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_inputFilePath))
        {
            UpdateStatus("错误：请先选择输入文件。", StatusType.Error);
            return;
        }

        FileSavePicker picker = new();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("所有文件", new List<string> { "." });
        picker.SuggestedFileName = Path.GetFileName(_outputFilePath) ?? "output";

        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        _outputFilePath = file.Path;
        OutputPathTextBox.Text = _outputFilePath;
        UpdateOperationButtons();
    }

    /// <summary>
    /// 点击"加密"按钮。
    /// </summary>
    private async void OnEncryptClick(object sender, RoutedEventArgs e)
    {
        await ExecuteCryptoOperationAsync(encrypt: true);
    }

    /// <summary>
    /// 点击"解密"按钮。
    /// </summary>
    private async void OnDecryptClick(object sender, RoutedEventArgs e)
    {
        await ExecuteCryptoOperationAsync(encrypt: false);
    }

    /// <summary>
    /// 执行加密或解密操作。
    /// </summary>
    /// <param name="encrypt">true 表示加密，false 表示解密。</param>
    private async Task ExecuteCryptoOperationAsync(bool encrypt)
    {
        // 输入校验
        if (string.IsNullOrEmpty(_inputFilePath) || !File.Exists(_inputFilePath))
        {
            UpdateStatus("错误：输入文件无效或不存在。", StatusType.Error);
            return;
        }

        if (string.IsNullOrEmpty(_outputFilePath))
        {
            UpdateStatus("错误：请设置输出路径。", StatusType.Error);
            return;
        }

        string password = PasswordBox.Password;
        if (string.IsNullOrEmpty(password))
        {
            UpdateStatus("错误：请输入密码。", StatusType.Error);
            return;
        }

        // 解密前检查文件头，给出更友好的错误提示
        if (!encrypt && !FileEncryptor.IsEncryptedFile(_inputFilePath))
        {
            UpdateStatus("错误：该文件不是 FileCryptoTool 生成的加密文件，无法解密。", StatusType.Error);
            return;
        }

        // UI 进入忙碌状态
        SetUiBusy(true);
        OperationProgressBar.Visibility = Visibility.Visible;
        OperationProgressBar.Value = 0;
        UpdateStatus(encrypt ? "正在加密..." : "正在解密...", StatusType.Processing);

        // 构造进度报告器，将进度回调调度到 UI 线程
        Progress<double> progress = new(value =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                OperationProgressBar.Value = value;
            });
        });

        try
        {
            if (encrypt)
            {
                await FileEncryptor.EncryptFileAsync(_inputFilePath, _outputFilePath, password, progress);
                UpdateStatus($"加密成功：{_outputFilePath}", StatusType.Success);
            }
            else
            {
                await FileEncryptor.DecryptFileAsync(_inputFilePath, _outputFilePath, password, progress);
                UpdateStatus($"解密成功：{_outputFilePath}", StatusType.Success);
            }
        }
        catch (InvalidDataException)
        {
            UpdateStatus("错误：文件头无效或文件已被篡改。", StatusType.Error);
        }
        catch (CryptographicException)
        {
            UpdateStatus("错误：密码错误或文件已损坏。", StatusType.Error);
        }
        catch (OperationCanceledException)
        {
            UpdateStatus("操作已取消。", StatusType.Info);
        }
        catch (Exception ex)
        {
            UpdateStatus($"错误：{ex.Message}", StatusType.Error);
        }
        finally
        {
            SetUiBusy(false);
        }
    }

    /// <summary>
    /// 根据输入文件路径推荐默认输出路径。
    /// </summary>
    /// <param name="inputPath">输入文件路径。</param>
    private void SuggestOutputPath(string inputPath)
    {
        string? directory = Path.GetDirectoryName(inputPath);
        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(inputPath);
        string extension = Path.GetExtension(inputPath);

        // 解密且扩展名为 .enc 时，去掉 .enc 后缀
        string suggestedFileName = extension.Equals(".enc", StringComparison.OrdinalIgnoreCase)
            ? $"{fileNameWithoutExt}.txt"
            : $"{fileNameWithoutExt}.enc";

        _outputFilePath = string.IsNullOrEmpty(directory)
            ? suggestedFileName
            : Path.Combine(directory, suggestedFileName);

        OutputPathTextBox.Text = _outputFilePath;
    }

    /// <summary>
    /// 设置 UI 忙碌/空闲状态。
    /// </summary>
    /// <param name="busy">true 表示忙碌，禁用操作按钮。</param>
    private void SetUiBusy(bool busy)
    {
        SelectInputButton.IsEnabled = !busy;
        SelectOutputButton.IsEnabled = !busy && !string.IsNullOrEmpty(_inputFilePath);
        EncryptButton.IsEnabled = !busy && !string.IsNullOrEmpty(_inputFilePath) && !string.IsNullOrEmpty(_outputFilePath);
        DecryptButton.IsEnabled = EncryptButton.IsEnabled;
        PasswordBox.IsEnabled = !busy;

        if (!busy)
        {
            OperationProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 根据当前输入状态刷新操作按钮可用性。
    /// </summary>
    private void UpdateOperationButtons()
    {
        bool canOperate = !string.IsNullOrEmpty(_inputFilePath) && !string.IsNullOrEmpty(_outputFilePath);
        EncryptButton.IsEnabled = canOperate;
        DecryptButton.IsEnabled = canOperate;
        SelectOutputButton.IsEnabled = !string.IsNullOrEmpty(_inputFilePath);
    }

    /// <summary>
    /// 更新状态文本。
    /// </summary>
    /// <param name="message">状态消息。</param>
    /// <param name="type">状态类型，决定文本颜色。</param>
    private void UpdateStatus(string message, StatusType type)
    {
        StatusTextBlock.Text = message;
        // 使用 TRAE 风格低饱和配色
        StatusTextBlock.Foreground = type switch
        {
            StatusType.Success => new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x4E, 0xC9, 0xB0)),
            StatusType.Error => new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xE5, 0x53, 0x4B)),
            StatusType.Processing => new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x4C, 0x9E, 0xD9)),
            _ => new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x9D, 0x9D, 0x9D))
        };
    }

    /// <summary>
    /// 状态类型枚举。
    /// </summary>
    private enum StatusType
    {
        Info,
        Success,
        Error,
        Processing
    }
}
