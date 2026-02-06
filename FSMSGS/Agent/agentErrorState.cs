using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSGS
{
    public class agentErrorState
    {
        public eEstopSysStatus estop_sys_status_last = eEstopSysStatus.eEstopHwSysOperational;
        public eSystemEstopReadyness eEstopReady_last = eSystemEstopReadyness.eReady;

        public ConcurrentDictionary<int, (E_OPCODES opcode, bool flag)> ErrorMap
        = new ConcurrentDictionary<int, (E_OPCODES opcode, bool flag)>();

        public bool _errorExistsInMsg = false;
        public E_OPCODES opcode = E_OPCODES.OP_NA;

        public int _num_of_non_scriptes_msgs = 0;
        public int _num_of_scriptes_msgs = 0;
        public byte _log_status = 0;

        public void updateDevice(int device, E_OPCODES opcode, bool flag, 
            eEstopSysStatus estop_sys_status, eSystemEstopReadyness eEstopReady,
            int num_of_non_scriptes_msgs, int num_of_scriptes_msgs, byte log_status)
        {
            if (IsMainDevice(device))
            {
                ErrorMap[device] = (opcode, flag);
            }
            estop_sys_status_last = estop_sys_status;
            eEstopReady_last = eEstopReady;
            _num_of_non_scriptes_msgs = num_of_non_scriptes_msgs;
            _num_of_scriptes_msgs = num_of_scriptes_msgs;
            _log_status = log_status;
        }
        public static bool IsMainDevice(int device)
        {
            return device == (int)DevicesScreen.MICB
                || device == (int)DevicesScreen.MOCB
                || device == (int)DevicesScreen.RC
                || device == (int)DevicesScreen.MC_FAST;
        }


    }
}
