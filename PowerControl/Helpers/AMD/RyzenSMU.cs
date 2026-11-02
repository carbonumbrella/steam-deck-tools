using CommonHelpers;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PowerControl.Helpers.AMD
{
    public class RyzenSMU : IDisposable
    {
        public IntPtr MMIO_ADDR;
        public uint MMIO_SIZE;
        public uint RES_ADDR, MSG_ADDR, PARAM_ADDR;
        public uint INDEX_ADDR, DATA_ADDR;

        IntPtr mappedAddress;
        IntPtr physicalHandle;
        InpOut? inpOut;

        public RyzenSMU()
        {
        }

        ~RyzenSMU()
        {
            Dispose();
        }

        public bool Opened
        {
            get { return physicalHandle != IntPtr.Zero && mappedAddress != IntPtr.Zero; }
        }

        public bool Open()
        {
            if (physicalHandle != IntPtr.Zero)
                return true;
            if (MMIO_ADDR == IntPtr.Zero || MMIO_SIZE == 0)
                return false;

            try
            {
                inpOut = new InpOut();
                mappedAddress = inpOut.MapPhysToLin(MMIO_ADDR, MMIO_SIZE, out physicalHandle);
            }
            catch (Exception e)
            {
                Log.Fatal("RyzenSMU", e);
                return false;
            }

            if (!Opened)
            {
                Log.Error("RyzenSMU: MapPhysToLin failed: addr={0:X}, size={1:X}, mapped={2:X}, handle={3:X}, driver: {4}, uptime={5}s",
                    (long)MMIO_ADDR, MMIO_SIZE, (long)mappedAddress, (long)physicalHandle, DriverState(), Environment.TickCount64 / 1000);
            }

            return Opened;
        }

        private static string DriverState()
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo("sc.exe", "query inpoutx64")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (process is null)
                    return "unknown";
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(2000);
                var line = output.Split('\n').FirstOrDefault(l => l.Contains("STATE") || l.Contains("Состояние"));
                return line?.Trim() ?? output.Trim().Replace('\n', ' ');
            }
            catch (Exception e)
            {
                return "error: " + e.Message;
            }
        }

        public void Dispose()
        {
            if (physicalHandle == IntPtr.Zero)
                return;

            GC.SuppressFinalize(this);
            inpOut?.UnmapPhysicalMemory(physicalHandle, mappedAddress);
            mappedAddress = IntPtr.Zero;
            physicalHandle = IntPtr.Zero;
            using (inpOut) { }
            inpOut = null;
        }

        private uint RregRaw(uint reg)
        {
            return (uint)Marshal.ReadInt32(mappedAddress, (int)reg);
        }

        private void WregRaw(uint reg, uint value)
        {
            Marshal.WriteInt32(mappedAddress, (int)reg, (int)value);
        }

        private bool WregCheckedRaw(uint reg, uint value)
        {
            WregRaw(reg, value);
            return RregRaw(reg) == value;
        }

        private bool Wreg(uint reg, uint value)
        {
            if (!Opened)
                return false;

            bool success = false;

            try
            {
                if (reg < MMIO_SIZE)
                {
                    return success = WregCheckedRaw(reg, value);
                }
                else
                {
                    if (!WregCheckedRaw(INDEX_ADDR, reg))
                        return false;
                    if (!WregCheckedRaw(DATA_ADDR, value))
                        return false;
                }

                return success = true;
            }
            finally
            {
                Log.Info("Wreg: reg={0:X}, value={1:X} => success={2}",
                       reg, value, success);
            }
        }

        private bool Rreg(uint reg, out uint value)
        {
            value = default;

            if (!Opened)
                return false;

            bool success = false;

            try
            {
                if (reg < MMIO_SIZE)
                {
                    value = RregRaw(reg);
                }
                else
                {
                    if (!WregCheckedRaw(INDEX_ADDR, reg))
                        return false;
                    value = RregRaw(DATA_ADDR);
                }

                return success = true;
            }
            finally
            {
                Log.Info("Rreg: reg={0:X} => read={1}/{1:X}, success={2}",
                       reg, value, success);
            }
        }

        private uint WaitForResponse()
        {
            const int timeout = 20;
            for (int i = 0; i < timeout; i++)
            {
                uint value;
                if (!Rreg(RES_ADDR, out value))
                    return 0;
                if (value != 0)
                    return value;
                Thread.SpinWait(100);
            }
            return 0;
        }

        public bool SendMsg(ushort msg, uint param)
        {
            return SendMsg(msg, param, out _);
        }

        public bool SendMsg(ushort msg, uint param, out uint arg)
        {
            bool success = false;

            arg = 0;

            try
            {
                var res = WaitForResponse();
                if (res != 0x1)
                {
                    // Reset SMU state
                    if (res != 0)
                        Wreg(RES_ADDR, 1);
                    return false;
                }

                Wreg(RES_ADDR, 0);
                Wreg(PARAM_ADDR, param);
                Wreg(MSG_ADDR, msg);

                res = WaitForResponse();
                if (res != 0x1)
                    return false;

                success = Rreg(PARAM_ADDR, out arg);
                return success;
            }
            finally
            {
                Log.Info(">> SendMsg: msg={0:X}, param={1:X} => arg={2}/{2:X}, success={3}",
                       msg, param, arg, success);
            }
        }

        public bool SendMsg<T>(T msg, uint param)
        {
            return SendMsg((ushort)(object)msg, param, out _);
        }

        public bool SendMsg<T>(T msg, uint param, out uint arg) where T : unmanaged
        {
            return SendMsg((ushort)(object)msg, param, out arg);
        }
    }
}
