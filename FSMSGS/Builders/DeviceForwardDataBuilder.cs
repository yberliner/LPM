using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZstdNet;

namespace MSGS
{
    public class DeviceForwardDataBuilder : IDisposable
    {
        private readonly List<byte[]> _uncompressedMsgs;
        private readonly int _numOfTimesToRepeat;
        private readonly int _repeatFirstLineCount;
        private readonly Compressor _compressor; // ZstdNet Compressor

        public DeviceForwardDataBuilder(List<byte[]> unCompressedMsgs, 
            int numOfTimesToRepeat, int repeatFirstLineCount)
        {
            if (unCompressedMsgs == null || unCompressedMsgs.Count == 0)
                throw new ArgumentException("unCompressedMsgs cannot be null or empty.");

            _uncompressedMsgs = unCompressedMsgs;
            _numOfTimesToRepeat = numOfTimesToRepeat;
            _repeatFirstLineCount = repeatFirstLineCount;

            _compressor = new Compressor();
        }

        public DeviceForwardData Build()
        {
            int totalCompressedSize = 0;
            int numMsgs = _uncompressedMsgs.Count;

            uint[] compressedSizes = new uint[numMsgs];
            uint[] uncompressedSizes = new uint[numMsgs];
            List<byte[]> compressedMsgs = new List<byte[]>(numMsgs);

            // First pass: compress all messages
            for (int i = 0; i < numMsgs; i++)
            {
                byte[] originalMsg = _uncompressedMsgs[i];

                byte[] compressedMsg = _compressor.Wrap(originalMsg);

                compressedMsgs.Add(compressedMsg);

                compressedSizes[i] = (uint)compressedMsg.Length;
                uncompressedSizes[i] = (uint)originalMsg.Length;

                totalCompressedSize += compressedMsg.Length;
            }

            // Second pass: combine all compressed messages into one buffer
            byte[] combinedCompressedBuffer = new byte[totalCompressedSize];
            int offset = 0;
            foreach (var compressedMsg in compressedMsgs)
            {
                compressedMsg.CopyTo(combinedCompressedBuffer, offset);
                offset += compressedMsg.Length;
            }

            return new DeviceForwardData
            {
                id = BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0),
                num_of_msgs = numMsgs,
                size_of_compressed_buffer = totalCompressedSize,
                numOfTimesToRepeat = _numOfTimesToRepeat,
                num_of_repeat_first_line = _repeatFirstLineCount,
                compressed_size_per_msg = compressedSizes,
                unCompressed_size_per_msg = uncompressedSizes,
                compressedBuffer = combinedCompressedBuffer
            };
        }

        public void Dispose()
        {
            _compressor?.Dispose();
        }
    }

}
