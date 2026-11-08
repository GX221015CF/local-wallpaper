using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

// Decode one local frame with Windows Media Foundation. No window, audio sink or playback engine.
internal static class VideoFrameDecoder {
#if TEST_BUILD
    internal static Action TestBeforeDecode;
#endif
    const int FirstVideoStream = unchecked((int)0xfffffffc);
    static readonly Guid MajorType = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    static readonly Guid Subtype = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    static readonly Guid Video = new Guid("73646976-0000-0010-8000-00aa00389b71");
    static readonly Guid Rgb32 = new Guid("00000016-0000-0010-8000-00aa00389b71");
    static readonly Guid FrameSize = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
    static readonly Guid DefaultStride = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    static readonly Guid VideoProcessing = new Guid("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");

    [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr reserved, uint mode);
    [DllImport("ole32.dll")] static extern void CoUninitialize();
    [DllImport("mfplat.dll")] static extern int MFStartup(uint version, uint flags);
    [DllImport("mfplat.dll")] static extern int MFShutdown();
    [DllImport("mfplat.dll")] static extern int MFCreateAttributes(out IntPtr attributes, int capacity);
    [DllImport("mfplat.dll")] static extern int MFCreateMediaType(out IntPtr mediaType);
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)]
    static extern int MFCreateSourceReaderFromURL(string path, IntPtr attributes, out IntPtr reader);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int SetGuid(IntPtr self, ref Guid key, ref Guid value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int SetUint(IntPtr self, ref Guid key, uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int GetUint(IntPtr self, ref Guid key, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int GetUlong(IntPtr self, ref Guid key, out ulong value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int SetType(IntPtr self, int stream, IntPtr reserved, IntPtr mediaType);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int GetMediaType(IntPtr self, int stream, out IntPtr mediaType);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int ReadSample(IntPtr self, int stream, uint controls, out int actualStream,
        out uint flags, out long time, out IntPtr sample);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int GetBuffer(IntPtr self, out IntPtr buffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int LockBuffer(IntPtr self, out IntPtr bytes, out uint maximum, out uint length);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int UnlockBuffer(IntPtr self);

    // Slots follow IMFAttributes, IMFSourceReader, IMFSample and IMFMediaBuffer in the Windows ABI.
    static T Method<T>(IntPtr value, int slot) where T : class {
        IntPtr function = Marshal.ReadIntPtr(Marshal.ReadIntPtr(value), slot * IntPtr.Size);
        return (T)(object)Marshal.GetDelegateForFunctionPointer(function, typeof(T));
    }

    static void Check(int result) {
        if (result < 0) Marshal.ThrowExceptionForHR(result);
    }

    static void Release(ref IntPtr value) {
        if (value != IntPtr.Zero) { Marshal.Release(value); value = IntPtr.Zero; }
    }

    static void Attribute(IntPtr type, Guid key, Guid value) {
        Check(Method<SetGuid>(type, 24)(type, ref key, ref value));
    }

    static Bitmap Image(IntPtr reader, IntPtr sample) {
        IntPtr type = IntPtr.Zero, buffer = IntPtr.Zero;
        bool locked = false;
        try {
            Check(Method<GetMediaType>(reader, 6)(reader, FirstVideoStream, out type));
            Guid sizeKey = FrameSize;
            ulong dimensions;
            Check(Method<GetUlong>(type, 8)(type, ref sizeKey, out dimensions));
            int width = checked((int)(dimensions >> 32)), height = checked((int)(dimensions & 0xffffffff));
            if (width < 1 || height < 1 || width > 8192 || height > 8192 || (long)width * height > 12000000)
                throw new InvalidDataException("视频画面尺寸超过取帧预算");
            Guid strideKey = DefaultStride;
            uint unsignedStride;
            int strideResult = Method<GetUint>(type, 7)(type, ref strideKey, out unsignedStride);
            int stride = strideResult < 0 ? checked(width * 4) : unchecked((int)unsignedStride);
            long rowBytes = Math.Abs((long)stride);
            if (rowBytes < (long)width * 4 || rowBytes > 65536)
                throw new InvalidDataException("视频像素行布局不受支持");
            Check(Method<GetBuffer>(sample, 41)(sample, out buffer));
            IntPtr bytes;
            uint maximum, length;
            Check(Method<LockBuffer>(buffer, 3)(buffer, out bytes, out maximum, out length));
            locked = true;
            if (rowBytes * height > length || length > 64 * 1048576)
                throw new InvalidDataException("视频解码缓冲区不完整或超过预算");
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);
            try {
                var pixels = bitmap.LockBits(new Rectangle(0, 0, width, height),
                    ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                try {
                    byte[] row = new byte[width * 4];
                    for (int y = 0; y < height; y++) {
                        int sourceRow = stride < 0 ? height - 1 - y : y;
                        Marshal.Copy(IntPtr.Add(bytes, checked((int)(sourceRow * rowBytes))), row, 0, row.Length);
                        Marshal.Copy(row, 0, IntPtr.Add(pixels.Scan0, y * pixels.Stride), row.Length);
                    }
                } finally { bitmap.UnlockBits(pixels); }
                return bitmap;
            } catch { bitmap.Dispose(); throw; }
        } finally {
            if (locked) Method<UnlockBuffer>(buffer, 4)(buffer);
            Release(ref buffer);
            Release(ref type);
        }
    }

    internal static Bitmap Decode(string localPath, Action check) {
#if TEST_BUILD
        if (TestBeforeDecode != null) TestBeforeDecode();
#endif
        if (!Path.IsPathRooted(localPath) || !File.Exists(localPath))
            throw new InvalidDataException("取帧需要已准备的本地视频");
        IntPtr attributes = IntPtr.Zero, reader = IntPtr.Zero, mediaType = IntPtr.Zero;
        int apartment = CoInitializeEx(IntPtr.Zero, 0);
        bool initialized = false;
        try {
            check();
            Check(MFStartup(0x20070, 1));
            initialized = true;
            Check(MFCreateAttributes(out attributes, 1));
            Guid processingKey = VideoProcessing;
            Check(Method<SetUint>(attributes, 21)(attributes, ref processingKey, 1));
            Check(MFCreateSourceReaderFromURL(localPath, attributes, out reader));
            Check(MFCreateMediaType(out mediaType));
            Attribute(mediaType, MajorType, Video);
            Attribute(mediaType, Subtype, Rgb32);
            Check(Method<SetType>(reader, 7)(reader, FirstVideoStream, IntPtr.Zero, mediaType));
            for (int attempt = 0; attempt < 45; attempt++) {
                check();
                IntPtr sample = IntPtr.Zero;
                try {
                    int stream;
                    uint flags;
                    long time;
                    Check(Method<ReadSample>(reader, 9)(reader, FirstVideoStream, 0,
                        out stream, out flags, out time, out sample));
                    check();
                    if (sample != IntPtr.Zero) {
                        Bitmap image = Image(reader, sample);
                        if (PreviewFrame.Informative(image)) return image;
                        image.Dispose();
                    }
                    if ((flags & 2) != 0) break;
                } finally { Release(ref sample); }
            }
            throw new InvalidDataException("未在开头画面中取得可识别的视频图片");
        } finally {
            Release(ref mediaType);
            Release(ref reader);
            Release(ref attributes);
            if (initialized) MFShutdown();
            if (apartment >= 0) CoUninitialize();
        }
    }
}
