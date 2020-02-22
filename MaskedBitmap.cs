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
        internal bool GetMaskedBool(int X = 0, int Y = 0) => (*(byte*)(Data.Scan0 + Index(X, Y, Data.Stride)) & Mask(X)) > 0;

        public Span<bool> GetImageLineAt(int y)
        {
            Span<bool> Line = new bool[Data.Width];
            for (int x = 0; x < Data.Width; x++)
            {
                Line[x] = GetMaskedBool(x, y);
            }
            return Line;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Image.Dispose();
        }

        public MaskedBitmap(Bitmap bitmap)
        {
            Image = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
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
            int failed = 2;

            void ThreadForward(object State)
            {
                AutoResetEvent Complete = (AutoResetEvent)State;

                Point RecursiveSearch(int TopDownToMid)
                {
                    ReadOnlySpan<bool> TopDownHay = Hay.GetImageLineAt(TopDownToMid);
                    int TopIndex = TopDownHay.IndexOf(Needle.GetImageLineAt(0));
                    if (TopIndex > 0 &&
                        Hay.GetImageLineAt(TopDownToMid + 1).Slice(TopIndex).IndexOf(Needle.GetImageLineAt(1)) == 0)
                    {
                        return new Point(TopIndex + (Needle.Data.Width / 2), TopDownToMid + (Needle.Data.Height / 2));
                    }
                    TopDownToMid++;
                    if (TopDownToMid < Hay.Data.Height /2)
                    {
                        return RecursiveSearch(TopDownToMid);
                    }
                    return default;
                }
                p = RecursiveSearch(0);
                if (p == default)
                {
                    if (failed == 2)
                    {
                        failed--;
                    }
                    else
                    {
                        Complete.Set();
                    }
                }
                else
                {
                    Complete.Set();
                }
            }

            void ThreadBackward(object State)
            {
                AutoResetEvent Complete = (AutoResetEvent)State;

                Point RecursiveSearch(int BottomUp)
                {
                    ReadOnlySpan<bool> BottomUpHay = Hay.GetImageLineAt(BottomUp);
                    int BottomUpIndex = BottomUpHay.IndexOf(Needle.GetImageLineAt(0));
                    if (BottomUpIndex > 0 &&
                        Hay.GetImageLineAt(BottomUp + 1).Slice(BottomUpIndex).IndexOf(Needle.GetImageLineAt(1)) == 0)
                    {
                        return new Point(BottomUpIndex + (Needle.Data.Width / 2), BottomUp + (Needle.Data.Height / 2));
                    }
                    BottomUp--;
                    if (BottomUp > Hay.Data.Height / 2)
                    {
                        RecursiveSearch(BottomUp);
                    }
                    return default;
                }
                p = RecursiveSearch(Hay.Data.Height -1);
                if (p == default)
                {
                    if (failed == 2)
                    {
                        failed--;
                    }
                    else
                    {
                        Complete.Set();
                    }
                }
                else
                {
                    Complete.Set();
                }
            }

            ThreadPool.QueueUserWorkItem(new WaitCallback(ThreadForward), waitHandle[0]);
            ThreadPool.QueueUserWorkItem(new WaitCallback(ThreadBackward), waitHandle[0]);
            WaitHandle.WaitAny(waitHandle);
            return p;
        }
    }
}
