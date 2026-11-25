using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace UltimateAlarm.Core
{
    public static class IpcProtocol
    {
        public const string PipeName = "UltimateAlarmPipe";
        private const int MaxPayloadBytes = 1024 * 1024; // 1 MB cap to prevent IPC memory DoS

        public static NamedPipeServerStream CreateServerStream()
        {
            PipeSecurity ps = new PipeSecurity();
            SecurityIdentifier systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            SecurityIdentifier adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            SecurityIdentifier interactiveSid = new SecurityIdentifier(WellKnownSidType.InteractiveSid, null);
            SecurityIdentifier authUsersSid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
            SecurityIdentifier networkSid = new SecurityIdentifier(WellKnownSidType.NetworkSid, null);

            // Explicitly block any remote SMB/network callers from reaching the local control pipe.
            ps.AddAccessRule(new PipeAccessRule(networkSid, PipeAccessRights.FullControl, AccessControlType.Deny));
            ps.AddAccessRule(new PipeAccessRule(systemSid, PipeAccessRights.FullControl, AccessControlType.Allow));
            ps.AddAccessRule(new PipeAccessRule(adminSid, PipeAccessRights.FullControl, AccessControlType.Allow));
            ps.AddAccessRule(new PipeAccessRule(interactiveSid, PipeAccessRights.ReadWrite, AccessControlType.Allow));
            ps.AddAccessRule(new PipeAccessRule(authUsersSid, PipeAccessRights.ReadWrite, AccessControlType.Allow));

            return new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                8,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                65536,
                65536,
                ps);
        }

        public static bool IsCallerAdminOrSystem(NamedPipeServerStream server)
        {
            if (server == null) return false;
            bool privileged = false;
            try
            {
                server.RunAsClient(delegate
                {
                    using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                    {
                        if (id != null)
                        {
                            if (id.IsSystem)
                            {
                                privileged = true;
                            }
                            else
                            {
                                WindowsPrincipal principal = new WindowsPrincipal(id);
                                privileged = principal.IsInRole(WindowsBuiltInRole.Administrator);
                            }
                        }
                    }
                });
            }
            catch
            {
                privileged = false;
            }
            return privileged;
        }

        public static ServiceStatusResponse SendRequest(IpcRequest req, int timeoutMs)
        {
            try
            {
                int ioTimeout = Math.Max(1500, timeoutMs * 2);
                using (NamedPipeClientStream client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    client.Connect(timeoutMs);
                    WriteMessage(client, JsonHelper.Serialize(req));
                    string respJson = ReadMessageWithTimeout(client, ioTimeout);
                    if (string.IsNullOrEmpty(respJson))
                    {
                        ServiceStatusResponse empty = new ServiceStatusResponse();
                        empty.Success = false;
                        empty.Error = "Empty or timed-out response from service.";
                        return empty;
                    }
                    return JsonHelper.Deserialize<ServiceStatusResponse>(respJson);
                }
            }
            catch (Exception ex)
            {
                ServiceStatusResponse err = new ServiceStatusResponse();
                err.Success = false;
                err.Error = ex.Message;
                return err;
            }
        }

        public static void WriteMessage(Stream stream, string json)
        {
            byte[] payload = Encoding.UTF8.GetBytes(json ?? "");
            if (payload.Length > MaxPayloadBytes)
            {
                throw new InvalidDataException("IPC payload exceeds maximum allowed size.");
            }
            byte[] lenBytes = BitConverter.GetBytes(payload.Length);
            stream.Write(lenBytes, 0, 4);
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }

        public static string ReadMessage(Stream stream)
        {
            return ReadMessageWithTimeout(stream, 4000);
        }

        public static string ReadMessageWithTimeout(Stream stream, int timeoutMs)
        {
            byte[] lenBytes = ReadExactWithTimeout(stream, 4, timeoutMs);
            if (lenBytes == null)
            {
                return null;
            }
            int len = BitConverter.ToInt32(lenBytes, 0);
            if (len < 0 || len > MaxPayloadBytes)
            {
                throw new InvalidDataException("Invalid IPC payload length: " + len);
            }
            if (len == 0)
            {
                return "";
            }
            byte[] payload = ReadExactWithTimeout(stream, len, timeoutMs);
            if (payload == null)
            {
                return null;
            }
            return Encoding.UTF8.GetString(payload);
        }

        private static byte[] ReadExactWithTimeout(Stream stream, int count, int timeoutMs)
        {
            byte[] buf = new byte[count];
            int offset = 0;
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(500, timeoutMs));
            while (offset < count)
            {
                int remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (remaining <= 0)
                {
                    return null;
                }
                IAsyncResult ar = stream.BeginRead(buf, offset, count - offset, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(remaining))
                {
                    return null;
                }
                int read = stream.EndRead(ar);
                if (read <= 0)
                {
                    return null;
                }
                offset += read;
            }
            return buf;
        }
    }
}
