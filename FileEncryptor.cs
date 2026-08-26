using System.Security.Cryptography;
using System.Text;

namespace FileCryptoTool;

/// <summary>
/// 文件加密/解密器。
/// 采用 AES-256-CBC 对称加密 + PKCS#7 填充 + PBKDF2(SHA-256) 密钥派生。
/// 加密文件格式：Magic(4) + Version(1) + Salt(16) + IV(16) + CipherText。
/// </summary>
public static class FileEncryptor
{
    // 文件头魔数，用于快速识别本工具生成的加密文件
    private static readonly byte[] FileMagic = Encoding.ASCII.GetBytes("FCT1");

    // 文件格式版本号，未来可扩展
    private const byte FileVersion = 1;

    // 文件头中用于识别格式的固定部分长度 = 魔数(4) + 版本(1)
    // 盐与 IV 由调用方单独读取
    private const int HeaderSize = 4 + 1;

    // 盐长度：128 位，确保每次加密密钥不同
    private const int SaltSize = 16;

    // IV 长度：128 位，等于 AES 块大小
    private const int IvSize = 16;

    // 密钥长度：256 位
    private const int KeySize = 32;

    // PBKDF2 迭代次数，兼顾安全性与性能
    private const int Pbkdf2Iterations = 10000;

    // 流式处理缓冲区大小，8 KB 为通用磁盘块倍数
    private const int ReadBufferSize = 8192;

    /// <summary>
    /// 判断指定文件是否为本工具生成的加密文件。
    /// </summary>
    /// <param name="filePath">待检查文件路径。</param>
    /// <returns>文件头魔数与版本匹配时返回 true；否则返回 false。</returns>
    public static bool IsEncryptedFile(string filePath)
    {
        // 文件必须存在且至少包含格式头 + 盐 + IV
        FileInfo info = new(filePath);
        if (!info.Exists || info.Length < HeaderSize + SaltSize + IvSize)
        {
            return false;
        }

        using FileStream stream = File.OpenRead(filePath);
        byte[] header = new byte[HeaderSize];

        // 读取固定格式头；若读不足直接判定为非法
        int read = stream.Read(header, 0, HeaderSize);
        if (read != HeaderSize)
        {
            return false;
        }

        // 校验魔数
        for (int i = 0; i < FileMagic.Length; i++)
        {
            if (header[i] != FileMagic[i])
            {
                return false;
            }
        }

        // 校验版本号
        return header[FileMagic.Length] == FileVersion;
    }

    /// <summary>
    /// 加密文件。
    /// </summary>
    /// <param name="inputPath">待加密的源文件路径。</param>
    /// <param name="outputPath">加密后输出文件路径。</param>
    /// <param name="password">加密密码。</param>
    /// <param name="progress">可选进度报告器，范围 0.0 ~ 1.0。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    /// <exception cref="ArgumentException">输入参数为空或无效。</exception>
    /// <exception cref="FileNotFoundException">源文件不存在。</exception>
    /// <exception cref="OperationCanceledException">操作被取消。</exception>
    public static async Task EncryptFileAsync(
        string inputPath,
        string outputPath,
        string password,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // 参数校验：路径与密码不能为空
        ValidatePaths(inputPath, outputPath);
        ValidatePassword(password);

        // 确认源文件存在
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("源文件不存在。", inputPath);
        }

        using FileStream inputStream = File.OpenRead(inputPath);
        using FileStream outputStream = File.Create(outputPath);

        // 生成随机盐与 IV
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] iv = RandomNumberGenerator.GetBytes(IvSize);

        // 写入文件头：魔数 + 版本 + 盐 + IV
        await outputStream.WriteAsync(FileMagic, cancellationToken);
        await outputStream.WriteAsync(new byte[] { FileVersion }, cancellationToken);
        await outputStream.WriteAsync(salt, cancellationToken);
        await outputStream.WriteAsync(iv, cancellationToken);

        // 派生 AES-256 密钥
        byte[] key = DeriveKey(password, salt, KeySize);

        // 初始化 AES 算法
        using Aes aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        // 使用 CryptoStream 包装输出流，进入加密写入模式
        using CryptoStream cryptoStream = new(outputStream, aes.CreateEncryptor(), CryptoStreamMode.Write);

        // 流式加密并报告进度
        await CopyWithProgressAsync(inputStream, cryptoStream, progress, cancellationToken);

        // 冲刷最终块，确保 PKCS#7 填充写入
        await cryptoStream.FlushFinalBlockAsync(cancellationToken);
    }

    /// <summary>
    /// 解密文件。
    /// </summary>
    /// <param name="inputPath">待解密的加密文件路径。</param>
    /// <param name="outputPath">解密后输出文件路径。</param>
    /// <param name="password">解密密码。</param>
    /// <param name="progress">可选进度报告器，范围 0.0 ~ 1.0。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    /// <exception cref="ArgumentException">输入参数为空或无效。</exception>
    /// <exception cref="FileNotFoundException">源文件不存在。</exception>
    /// <exception cref="InvalidDataException">文件头无效或文件被篡改。</exception>
    /// <exception cref="CryptographicException">密码错误或密文损坏。</exception>
    /// <exception cref="OperationCanceledException">操作被取消。</exception>
    public static async Task DecryptFileAsync(
        string inputPath,
        string outputPath,
        string password,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // 参数校验
        ValidatePaths(inputPath, outputPath);
        ValidatePassword(password);

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("加密文件不存在。", inputPath);
        }

        using FileStream inputStream = File.OpenRead(inputPath);

        // 读取并校验文件头
        await ValidateHeaderAsync(inputStream, cancellationToken);

        // 读取盐与 IV
        byte[] salt = new byte[SaltSize];
        byte[] iv = new byte[IvSize];
        await inputStream.ReadExactlyAsync(salt.AsMemory(0, SaltSize), cancellationToken);
        await inputStream.ReadExactlyAsync(iv.AsMemory(0, IvSize), cancellationToken);

        // 派生密钥
        byte[] key = DeriveKey(password, salt, KeySize);

        // 初始化 AES 算法
        using Aes aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        // 使用 ProgressStream 包装输入流，使解密进度基于密文字节数
        using ProgressStream progressStream = new(inputStream, inputStream.Length, progress);
        using CryptoStream cryptoStream = new(progressStream, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using FileStream outputStream = File.Create(outputPath);

        // 流式解密
        await cryptoStream.CopyToAsync(outputStream, ReadBufferSize, cancellationToken);

        // 显式冲刷输出流，确保数据落盘
        await outputStream.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// 读取并校验加密文件固定格式头（魔数 + 版本）。
    /// </summary>
    /// <param name="stream">已打开的加密文件流，位置必须在文件开头。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="InvalidDataException">文件头魔数或版本不匹配。</exception>
    private static async Task ValidateHeaderAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] header = new byte[HeaderSize];
        await stream.ReadExactlyAsync(header.AsMemory(0, HeaderSize), cancellationToken);

        // 校验魔数
        for (int i = 0; i < FileMagic.Length; i++)
        {
            if (header[i] != FileMagic[i])
            {
                throw new InvalidDataException("文件头魔数不匹配，该文件不是本工具生成的加密文件。");
            }
        }

        // 校验版本号（魔数之后的第一字节）
        if (header[FileMagic.Length] != FileVersion)
        {
            throw new InvalidDataException($"不支持的加密文件版本：{header[FileMagic.Length]}。");
        }
    }

    /// <summary>
    /// 从密码和盐派生指定长度的密钥。
    /// </summary>
    /// <param name="password">用户密码。</param>
    /// <param name="salt">随机盐。</param>
    /// <param name="keyLength">期望密钥长度（字节）。</param>
    /// <returns>派生后的密钥字节数组。</returns>
    private static byte[] DeriveKey(string password, byte[] salt, int keyLength)
    {
        using Rfc2898DeriveBytes deriveBytes = new(
            password,
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256);

        return deriveBytes.GetBytes(keyLength);
    }

    /// <summary>
    /// 带进度报告的流复制。
    /// </summary>
    /// <param name="source">源流。</param>
    /// <param name="destination">目标流。</param>
    /// <param name="progress">进度报告器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task CopyWithProgressAsync(
        Stream source,
        Stream destination,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        long totalBytes = source.Length;
        long processedBytes = 0;
        byte[] buffer = new byte[ReadBufferSize];

        while (true)
        {
            // 从源文件读取明文块
            int bytesRead = await source.ReadAsync(
                buffer.AsMemory(0, ReadBufferSize),
                cancellationToken);

            if (bytesRead == 0)
            {
                break;
            }

            // 写入加密流
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);

            // 更新进度
            processedBytes += bytesRead;
            progress?.Report((double)processedBytes / totalBytes);
        }
    }

    /// <summary>
    /// 校验文件路径参数。
    /// </summary>
    /// <param name="inputPath">输入路径。</param>
    /// <param name="outputPath">输出路径。</param>
    /// <exception cref="ArgumentException">路径为空或仅空白。</exception>
    private static void ValidatePaths(string inputPath, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException("输入文件路径不能为空。", nameof(inputPath));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("输出文件路径不能为空。", nameof(outputPath));
        }
    }

    /// <summary>
    /// 校验密码参数。
    /// </summary>
    /// <param name="password">密码。</param>
    /// <exception cref="ArgumentException">密码为空。</exception>
    private static void ValidatePassword(string password)
    {
        if (password is null)
        {
            throw new ArgumentException("密码不能为空。", nameof(password));
        }
    }

    /// <summary>
    /// 进度包装流。
    /// 用于解密场景：CryptoStream 会从此流读取密文，因此报告的是密文读取进度。
    /// </summary>
    private sealed class ProgressStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _totalLength;
        private readonly IProgress<double>? _progress;
        private long _readBytes;

        /// <summary>
        /// 初始化进度包装流。
        /// </summary>
        /// <param name="inner">底层流。</param>
        /// <param name="totalLength">总长度，用于计算进度百分比。</param>
        /// <param name="progress">进度报告器。</param>
        public ProgressStream(Stream inner, long totalLength, IProgress<double>? progress)
        {
            _inner = inner;
            _totalLength = totalLength;
            _progress = progress;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = _inner.Read(buffer, offset, count);
            ReportProgress(read);
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int read = await _inner.ReadAsync(buffer, offset, count, cancellationToken);
            ReportProgress(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await _inner.ReadAsync(buffer, cancellationToken);
            ReportProgress(read);
            return read;
        }

        public override void Flush() => _inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void SetLength(long value) => _inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        /// <summary>
        /// 报告读取进度。
        /// </summary>
        /// <param name="read">本次读取的字节数。</param>
        private void ReportProgress(int read)
        {
            if (read <= 0 || _progress is null || _totalLength <= 0)
            {
                return;
            }

            _readBytes += read;
            double ratio = (double)_readBytes / _totalLength;
            _progress.Report(Math.Min(ratio, 1.0));
        }
    }
}
