using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSGS
{
    public static class BufferValidator
    {
        private static int _callCount = 0;
        //private static readonly Stopwatch _stopwatch = new();
        private static readonly byte[] _zeroBuffer = new byte[5000]; // zero-initialized


        public static bool IsZeroedFromIndex(byte[] buf, int startIndex, 
            ref float badPackets, ref float goodPackets)
        {
            return true;

            ////This function is for checking the length of buffer to make sure we read everything
            ////It is for debug porposes
            //if (startIndex >= buf.Length)
            //    return true;

            ////_stopwatch.Start();

            //int len = buf.Length - startIndex;
            //Span<byte> span = buf.AsSpan(startIndex);
            //Span<byte> zeroSpan = _zeroBuffer.AsSpan(0, len);

            //if (!span.SequenceEqual(zeroSpan))
            //{
            //    badPackets++;
            //    Console.WriteLine($"Msg Not Zero from End. Good / Bad UDP packets ratio is: {(goodPackets == 0 ? 0 : badPackets / goodPackets)}");
            //    MaybePrintAverage();
            //    //_stopwatch.Stop();
            //    return false;
            //}

            //goodPackets++;
            ////_stopwatch.Stop();
            ////MaybePrintAverage();
            //return true;
        }

        private static void MaybePrintAverage()
        {
            if (_callCount++ >= 100 )
            {
                //Console.WriteLine($"Total processing time for IsZeroedFromIndex: {_stopwatch.Elapsed.TotalMilliseconds} ms");
                _callCount = 0;
            }
        }
    }
}
