using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MSGS
{
    public static class MessageCreator
    {
        public static byte[] CreateForwardMessage(int _device, 
            int deviceTypeMsg, ref List<byte[]> msgs,
            out DeviceForwardData data, Cmd new_cmd, int num_of_loops = 1)
        {
            int num_of_times_to_repeat = num_of_loops;
            int num_of_repeat_first_line = 1;

            DeviceMsg device_msg = new()
            {
                device = _device,
                cmd = (int)new_cmd
                //data_len = (uint)Marshal.SizeOf<DeviceForwardData>()
            };
            
            DeviceForwardDataBuilder builder = new DeviceForwardDataBuilder(
                msgs, num_of_times_to_repeat, num_of_repeat_first_line);

            data = builder.Build();
             
            byte[] data_buffer = DeviceForwardDataPacker.PackDeviceForwardData(data);
            
            device_msg.data_len = (uint)data_buffer.Length;
            byte[] device_msg_Bytes = MSGHelper.StructureToByteArray(device_msg);

            return device_msg_Bytes.Concat(data_buffer).ToArray();
        }

        public static byte[] CreateInitDeviceMessage(int _device, int start_stop, string ip,
            int port, int _listenPort, int delayMicroSeconds)
        {
            DeviceInitData init_data = new()
            {
                IP = MSGHelper.StringToFixedSizeBytes(ip, 15),
                listen_port = (uint)_listenPort,
                out_port = (uint)port,
                micro_sec_delay = (uint)delayMicroSeconds
            };
            byte[] init_data_Bytes = MSGHelper.StructureToByteArray(init_data);


            DeviceMsg device_msg = new()
            {
                device = _device,
                cmd = (int)Cmd.InitDevice,
                data_len = (uint)init_data_Bytes.Length
            };
            byte[] device_msg_Bytes = MSGHelper.StructureToByteArray(device_msg);

            
            return device_msg_Bytes.Concat(init_data_Bytes).ToArray();

            //InitDevice initDevice = new InitDevice
            //{
            //    start_stop = start_stop,
            //    ip = ip,
            //    port = port,
            //    receive_port = listenPort,
            //    delay_time_micro_seconds = delayMicroSeconds
            //};

            //byte[] initBytes = MSGHelper.StructureToByteArray(initDevice);

            //return new ServerMsg
            //{
            //    device = device,
            //    device_type_msg = 0,
            //    _msg_sequence = 42,
            //    buffer_max_len = 1500,
            //    size_of_data = initBytes.Length,
            //    cmd = Cmd.InitDevice,
            //    buffer = CreateBuffer(initBytes)
            //};
        }

        private static byte[] CreateBuffer(byte[] data)
        {
            var buffer = new byte[1500];
            Array.Copy(data, buffer, data.Length);
            return buffer;
        }
    }
}
