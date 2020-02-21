using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;

namespace ActiveScreenMatch
{
    public sealed unsafe class MaskedBitmap : IDisposable
    {
        internal Bitmap Image { get; }
        internal BitmapData Data { get; }

        internal static int Index(int X, int Y, int Stride) => (Y * Stride) + (X >> 3);
        internal static byte Mask(int X) => (byte)(0x80 >> (X & 0x7));

        public Span<bool> GetImageLineAt(int y)
        {
            Span<bool> Line = new bool[Data.Width];
            for (int x = 0; x < Data.Width; x++)
            {
                Line[x] = GetMaskedBool(x, y);
            }
            return Line;
        }

        public bool GetMaskedBool(int X = 0, int Y = 0) => (*(byte*)(Data.Scan0 + Index(X, Y, Data.Stride)) & Mask(X)) > 0;

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Image.Dispose();
        }

        public MaskedBitmap(Bitmap bitmap)
        {
            if (bitmap == null)
                throw new System.ArgumentNullException(nameof(bitmap));

            Image = bitmap;
            Data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format1bppIndexed);
        }

        ~MaskedBitmap()
        {
            Image.UnlockBits(Data);
        }
    }

    public static class MaskedBitmapExtensions
    {
        public static Point MaskedBitmapSearch(this MaskedBitmap Hay, Bitmap Needle)
        {
            Point p = default;
            if (Needle == null || Hay == null)
            {
                return default;
            }
            else
            {
                using (var MaskedNeedle = new MaskedBitmap(Needle))
                {
                    p = Hay.SearchRoutine(MaskedNeedle);
                }
            }
            return p;
        }

        public static Point SearchRoutine(this MaskedBitmap Hay, MaskedBitmap Needle)
        {
            WaitHandle[] waitHandle = new WaitHandle[]
            {
                new AutoResetEvent(false)
            };

            Point p = default;

            void ThreadForward(object State)
            {
                AutoResetEvent Complete = (AutoResetEvent)State;
                int HeightUp = 0;
                while (HeightUp < Hay.Data.Height)
                {
                    ReadOnlySpan<bool> TopDownHay = Hay.GetImageLineAt(HeightUp);
                    int BottomIndex = TopDownHay.IndexOf(Needle.GetImageLineAt(0));
                    if (BottomIndex > 0 &&
                        Hay.GetImageLineAt(HeightUp + 1).Slice(BottomIndex).IndexOf(Needle.GetImageLineAt(1)) == 0)
                    {
                        p = new Point(BottomIndex + (Needle.Data.Width / 2), HeightUp + (Needle.Data.Height / 2));
                        Complete.Set();
                        return;
                    }
                    else
                    {
                        HeightUp++;
                    }
                }
            }

            void ThreadBackward(object State)
            {
                AutoResetEvent Complete = (AutoResetEvent)State;
                int HeightDown = Hay.Data.Height;
                while (HeightDown > 0)
                {
                    Span<bool> BottomUpHay = Hay.GetImageLineAt(HeightDown);
                    int BottomIndex = BottomUpHay.IndexOf(Needle.GetImageLineAt(0));
                    if (BottomIndex > 0 &&
                        Hay.GetImageLineAt(HeightDown + 1).Slice(BottomIndex).IndexOf(Needle.GetImageLineAt(1)) == 0)
                    {
                        p = new Point(BottomIndex + (Needle.Data.Width / 2), HeightDown + (Needle.Data.Height / 2));
                        Complete.Set();
                        return;
                    }
                    else
                    {
                        HeightDown--;
                    }
                }
            }

            ThreadPool.QueueUserWorkItem(new WaitCallback(ThreadForward), waitHandle[0]);
            ThreadPool.QueueUserWorkItem(new WaitCallback(ThreadBackward), waitHandle[0]);
            WaitHandle.WaitAny(waitHandle);
            return p;
        }
    }
}
