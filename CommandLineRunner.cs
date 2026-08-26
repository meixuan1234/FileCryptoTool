using System.IO;
using System.Security.Cryptography;

namespace FileCryptoTool;

/// <summary>
/// 命令行模式执行器。
/// 支持无界面方式执行加密/解密，方便批量处理或生成测试文件。
/// </summary>
internal static class CommandLineRunner
{
    /// <summary>
    /// 解析命令行参数并执行对应操作。
    /// </summary>
    /// <param name="args">命令行参数数组。</param>
    /// <returns>退出码：0 成功，1 参数错误，2 操作失败。</returns>
    public static int Run(string[] args)
    {
        // 至少需要 mode + input + output + password
        if (args.Length < 5)
        {
            PrintUsage();
            return 1;
        }

        string mode = args[1].ToLowerInvariant();
        string inputPath = args[2];
        string outputPath = args[3];
        string password = args[4];

        try
        {
            if (mode == "encrypt" || mode == "e")
            {
                // 在线程池线程上执行异步加密，避免 WinUI STA 同步上下文死锁
                Task.Run(() => FileEncryptor.EncryptFileAsync(inputPath, outputPath, password)).GetAwaiter().GetResult();
                Console.WriteLine($"加密成功：{outputPath}");
            }
            else if (mode == "decrypt" || mode == "d")
            {
                // 在线程池线程上执行异步解密，避免 WinUI STA 同步上下文死锁
                Task.Run(() => FileEncryptor.DecryptFileAsync(inputPath, outputPath, password)).GetAwaiter().GetResult();
                Console.WriteLine($"解密成功：{outputPath}");
            }
            else
            {
                PrintUsage();
                return 1;
            }

            return 0;
        }
        catch (InvalidDataException ex)
        {
            Console.WriteLine($"错误：{ex.Message}");
            return 2;
        }
        catch (CryptographicException)
        {
            Console.WriteLine("错误：密码错误或文件已损坏。");
            return 2;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"错误：{ex.Message}");
            return 2;
        }
    }

    /// <summary>
    /// 打印命令行用法。
    /// </summary>
    private static void PrintUsage()
    {
        Console.WriteLine("用法：FileCryptoTool <encrypt|decrypt> <输入文件> <输出文件> <密码>");
        Console.WriteLine("示例：FileCryptoTool encrypt plain.txt plain.txt.enc 20310710");
        Console.WriteLine("      FileCryptoTool decrypt plain.txt.enc plain.txt 20310710");
    }
}
