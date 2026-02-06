using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace MSGS
{
    public static class DeviceForwardDataPacker
    {
        public static byte[] PackDeviceForwardData(DeviceForwardData data)
        {
            if (data.num_of_msgs <= 0 || data.compressedBuffer == null)
                throw new ArgumentException("Invalid DeviceForwardData input.");

            int headerSize = sizeof(int) + //ID 
                             sizeof(int) + //num of msgs
                             sizeof(int) + //size_of_compressed_buffer
                             sizeof(int) + //numOfTimesToRepeat
                             sizeof(int);  //num_of_repeat_first_line 4*5 = 20 Bytes.

            int totalSize =
                headerSize +
                (data.num_of_msgs * sizeof(uint)) +      // compressed_size_per_msg array
                (data.num_of_msgs * sizeof(uint)) +      // unCompressed_size_per_msg array
                data.size_of_compressed_buffer;          // compressedBuffer

            byte[] result = new byte[totalSize];
            int offset = 0;
           
            offset = PackHeaders(data, result, offset);
            
            offset = PackArrays(data, result, offset);

            // 4. Final validation (safety)
            if (offset != totalSize)
                throw new InvalidOperationException($"Packing error: offset ({offset}) != total size ({totalSize})");

            return result;
        }

        private static int PackArrays(DeviceForwardData data, byte[] result, int offset)
        {
            // 1. Copy compressed_size_per_msg array
            Buffer.BlockCopy(data.compressed_size_per_msg, 0, result, offset, data.num_of_msgs * sizeof(uint));
            offset += data.num_of_msgs * sizeof(uint);

            // 2. Copy unCompressed_size_per_msg array
            Buffer.BlockCopy(data.unCompressed_size_per_msg, 0, result, offset, data.num_of_msgs * sizeof(uint));
            offset += data.num_of_msgs * sizeof(uint);

            // 3. Copy compressedBuffer
            Buffer.BlockCopy(data.compressedBuffer, 0, result, offset, data.compressedBuffer.Length);
            offset += data.compressedBuffer.Length;
            return offset;
        }

        private static int PackHeaders(DeviceForwardData data, byte[] result, int offset)
        {

            // 1. Copy num_of_msgs
            Buffer.BlockCopy(BitConverter.GetBytes(data.id), 0, result, offset, sizeof(int));
            offset += sizeof(int);

            // 2. Copy num_of_msgs
            Buffer.BlockCopy(BitConverter.GetBytes(data.num_of_msgs), 0, result, offset, sizeof(int));
            offset += sizeof(int);

            // 4. Copy size_of_compressed_buffer
            Buffer.BlockCopy(BitConverter.GetBytes(data.size_of_compressed_buffer), 0, result, offset, sizeof(int));
            offset += sizeof(int);

            // 5. Copy isCyclic (byte)
            Buffer.BlockCopy(BitConverter.GetBytes(data.numOfTimesToRepeat), 0, result, offset, sizeof(int));
            offset += sizeof(int);

            // 5. Copy num_of_repeat_first_line
            Buffer.BlockCopy(BitConverter.GetBytes(data.num_of_repeat_first_line), 0, result, offset, sizeof(int));
            offset += sizeof(int);
            return offset;
        }
    }
}
