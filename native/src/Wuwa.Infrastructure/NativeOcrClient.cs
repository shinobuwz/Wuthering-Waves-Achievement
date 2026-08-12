using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Wuwa.Infrastructure;

public sealed record NativeOcrOptions(
    string RecognitionModelPath,
    string CharacterDictionaryPath,
    int IntraOpThreads = 4,
    int InterOpThreads = 1,
    int RecognitionHeight = 48,
    int RecognitionMinWidth = 320,
    int RecognitionMaxWidth = 1920,
    float MinimumScore = 0.0f,
    bool IncludeSpaceCharacter = true);

public sealed record NativeOcrRecognition(string Text, float Score);

/// <summary>Safe managed owner for the project-specific Wuwa.Ocr.Native C ABI.</summary>
public sealed partial class NativeOcrClient : IDisposable
{
    private const string NativeLibraryName = "Wuwa.Ocr.Native";
    private const uint ExpectedAbiVersion = 1;
    private readonly SafeOcrHandle _handle;
    private readonly object _syncRoot = new();
    private bool _disposed;

    static NativeOcrClient()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeOcrClient).Assembly, ResolveNativeLibrary);
    }

    public NativeOcrClient(NativeOcrOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Native OCR currently supports Windows x64 only.");
        if (!Environment.Is64BitProcess) throw new PlatformNotSupportedException("Native OCR requires an x64 process.");
        if (string.IsNullOrWhiteSpace(options.RecognitionModelPath)) throw new ArgumentException("Recognition model path is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.CharacterDictionaryPath)) throw new ArgumentException("Character dictionary path is required.", nameof(options));

        var abi = NativeMethods.AbiVersion();
        if (abi != ExpectedAbiVersion) throw new InvalidOperationException($"Native OCR ABI {abi} is incompatible with expected ABI {ExpectedAbiVersion}.");

        var modelPath = Marshal.StringToHGlobalUni(Path.GetFullPath(options.RecognitionModelPath));
        var dictionaryPath = Marshal.StringToHGlobalUni(Path.GetFullPath(options.CharacterDictionaryPath));
        try
        {
            var config = new NativeConfig
            {
                AbiVersion = ExpectedAbiVersion,
                RecognitionModelPath = modelPath,
                CharacterDictionaryPath = dictionaryPath,
                IntraOpThreads = options.IntraOpThreads,
                InterOpThreads = options.InterOpThreads,
                RecognitionHeight = options.RecognitionHeight,
                RecognitionMinWidth = options.RecognitionMinWidth,
                RecognitionMaxWidth = options.RecognitionMaxWidth,
                MinimumScore = options.MinimumScore,
                IncludeSpaceCharacter = options.IncludeSpaceCharacter ? 1 : 0
            };
            var status = NativeMethods.Create(in config, out var rawHandle);
            if (status != NativeOcrStatus.Ok || rawHandle == IntPtr.Zero)
            {
                throw new NativeOcrException(status, ReadLastError(IntPtr.Zero));
            }
            _handle = new SafeOcrHandle(rawHandle);
        }
        finally
        {
            Marshal.FreeHGlobal(modelPath);
            Marshal.FreeHGlobal(dictionaryPath);
        }
    }

    public static uint AbiVersion => NativeMethods.AbiVersion();

    public static string Version => Marshal.PtrToStringUTF8(NativeMethods.Version()) ?? string.Empty;

    public unsafe NativeOcrRecognition RecognizeBgr(ReadOnlySpan<byte> pixels, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0 || stride < checked(width * 3)) throw new ArgumentOutOfRangeException(nameof(width), "A valid packed BGR image is required.");
        if (pixels.Length < checked(stride * height)) throw new ArgumentException("Pixel buffer is smaller than stride × height.", nameof(pixels));

        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            fixed (byte* pointer = pixels)
            {
                var status = NativeMethods.RecognizeBgr(_handle, (IntPtr)pointer, width, height, stride, out var result);
                if (status != NativeOcrStatus.Ok) throw new NativeOcrException(status, ReadLastError(_handle.DangerousGetHandle()));
                return new NativeOcrRecognition(Marshal.PtrToStringUTF8(result.TextUtf8) ?? string.Empty, result.Score);
            }
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed) return;
            _handle.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    private static string ReadLastError(IntPtr handle)
    {
        var status = NativeMethods.LastError(handle, null, 0, out var required);
        if (status is not (NativeOcrStatus.Ok or NativeOcrStatus.BufferTooSmall) || required <= 1) return "Native OCR operation failed.";
        var buffer = new byte[required];
        status = NativeMethods.LastError(handle, buffer, buffer.Length, out _);
        return status == NativeOcrStatus.Ok ? Encoding.UTF8.GetString(buffer.AsSpan(0, buffer.Length - 1)) : "Native OCR operation failed.";
    }

    private static IntPtr ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, NativeLibraryName, StringComparison.Ordinal)) return IntPtr.Zero;
        var configuredRoot = Environment.GetEnvironmentVariable("WUWA_NATIVE_OCR_ROOT");
        var candidates = new[]
        {
            string.IsNullOrWhiteSpace(configuredRoot) ? null : Path.Combine(configuredRoot, NativeLibraryName + ".dll"),
            Path.Combine(AppContext.BaseDirectory, "ocr", NativeLibraryName + ".dll"),
            Path.Combine(AppContext.BaseDirectory, NativeLibraryName + ".dll")
        };
        foreach (var candidate in candidates)
        {
            if (candidate is not null && File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle)) return handle;
        }
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeConfig
    {
        public uint AbiVersion;
        public IntPtr RecognitionModelPath;
        public IntPtr CharacterDictionaryPath;
        public int IntraOpThreads;
        public int InterOpThreads;
        public int RecognitionHeight;
        public int RecognitionMinWidth;
        public int RecognitionMaxWidth;
        public float MinimumScore;
        public int IncludeSpaceCharacter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeResult
    {
        public readonly IntPtr TextUtf8;
        public readonly float Score;
    }

    private sealed class SafeOcrHandle : SafeHandle
    {
        public SafeOcrHandle() : base(IntPtr.Zero, true) { }
        public SafeOcrHandle(IntPtr handle) : this() => SetHandle(handle);
        public override bool IsInvalid => handle == IntPtr.Zero;
        protected override bool ReleaseHandle()
        {
            NativeMethods.Destroy(handle);
            return true;
        }
    }

    private static partial class NativeMethods
    {
        private const string Library = "Wuwa.Ocr.Native";

        [LibraryImport(Library, EntryPoint = "wuwa_ocr_abi_version")]
        [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
        internal static partial uint AbiVersion();

        [LibraryImport(Library, EntryPoint = "wuwa_ocr_version")]
        [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
        internal static partial IntPtr Version();

        [LibraryImport(Library, EntryPoint = "wuwa_ocr_create")]
        [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
        internal static partial NativeOcrStatus Create(in NativeConfig config, out IntPtr handle);

        [LibraryImport(Library, EntryPoint = "wuwa_ocr_destroy")]
        [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
        internal static partial void Destroy(IntPtr handle);

        [LibraryImport(Library, EntryPoint = "wuwa_ocr_recognize_bgr")]
        [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
        internal static partial NativeOcrStatus RecognizeBgr(SafeOcrHandle handle, IntPtr pixels, int width, int height, int stride, out NativeResult result);

        [LibraryImport(Library, EntryPoint = "wuwa_ocr_last_error")]
        [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
        internal static partial NativeOcrStatus LastError(IntPtr handle, byte[]? buffer, int bufferSize, out int requiredSize);
    }
}

public enum NativeOcrStatus
{
    Ok = 0,
    InvalidArgument = 1,
    IoError = 2,
    ModelError = 3,
    InferenceError = 4,
    BufferTooSmall = 5,
    InternalError = 6
}

public sealed class NativeOcrException : Exception
{
    internal NativeOcrException(NativeOcrStatus status, string message) : base($"{message} (native status: {status})") => Status = status;

    public NativeOcrStatus Status { get; }
}
