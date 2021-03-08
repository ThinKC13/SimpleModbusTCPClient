using System;
using System.Net.Sockets;

namespace SmallModbusTcpClient
{
    class ProtocolDataUnit
    {
        private byte functionCode;
        /// <summary>
        /// Public or user defined function codes per specification.
        /// </summary>
        public byte FunctionCode
        {
            get { return functionCode; }
            set
            {
                if(value >= 1 && value <= 4) { functionCode = value; }
                else { throw new NotImplementedException("Function not implemented."); }
            }
        }

        /// <summary>
        /// Function code from response should match request function code otherwise error code.
        /// </summary>
        public byte ResponseFunctionCode { get; set; }

        private ushort startingAddress;
        /// <summary>
        /// Data address of first register requested in hexadecimal.
        /// </summary>
        public ushort StartingAddress
        {
            get { return startingAddress; }
            set
            {
                if (value >= 0x0000 && value <= 0xFFFF) { startingAddress = value; }
                else { throw new ArgumentOutOfRangeException(nameof(value), "Invalid value. Value must be between 0 and 65535"); }
            }
        }

        
        private ushort registerQuantity;
        /// <summary>
        /// Total number of registers requested.
        /// </summary>
        public ushort RegisterQuantity
        {
            get { return registerQuantity; }
            set
            {
                if(FunctionCode == 1 || FunctionCode == 2)
                {
                    if (value >= 0x0001 && value <= 0x07D0) { registerQuantity = value; }
                    else { throw new ArgumentOutOfRangeException(nameof(value), "Invalid value. Value must be between 1 and 2000"); }
                }
                else if(FunctionCode == 3 || FunctionCode == 4)
                {
                    if (value >= 0x0001 && value <= 0x007D) { registerQuantity = value; }
                    else { throw new ArgumentOutOfRangeException(nameof(value), "Invalid value. Value must be between 1 and 125"); }
                }
                
            }
        }

        /// <summary>
        /// Data in PDU starting address + register quantity in bigEndian.
        /// </summary>
        public byte[] RequestData
        {
            get
            {
                Byte[] data = new byte[]
                {
                    BitConverter.GetBytes((ushort)StartingAddress)[1],
                    BitConverter.GetBytes((ushort)StartingAddress)[0],
                    BitConverter.GetBytes((ushort)RegisterQuantity)[1],
                    BitConverter.GetBytes((ushort)RegisterQuantity)[0],
                };

                return data;
            }
        }

        /// <summary>
        /// Reponse byte count
        /// </summary>
        public byte ByteCount { get; set; }

        /// <summary>
        /// Data category from response
        /// </summary>
        public byte[] ResponseData { get; set; }

        /// <summary>
        /// Written to when reading coils Fc 0x01
        /// </summary>
        public bool[] BoolRegister { get; set; }
        public ushort[] ByteRegister { get; set; }

        public void ParseResponse()
        {
            // assign results to proper register
            // booleans
            if (FunctionCode == 1 || FunctionCode == 2)
            {
                BoolRegister = new bool[RegisterQuantity];
                for (int i = 0; i < RegisterQuantity; i++)
                {
                    // stays within current byte and get byte value
                    int intByte = ResponseData[i / 8];

                    // determine which bit being looked with a bit mask
                    int bitMask = Convert.ToInt32(Math.Pow(2, (i % 8)));

                    // logical AND operator on int to determine which bits are set (1) or clear (0) with division by mask to get either 1 or 0
                    BoolRegister[i] = Convert.ToBoolean((intByte & bitMask) / bitMask);

                    // debug
                    // Console.WriteLine(String.Format("i: {0} intData: {1} mask: {2} (bit)AND w/ mask: {3} result: {4}", i, intByte, bitMask, (intByte & bitMask) / bitMask, BoolRegister[i]));
                }
            }
            else if(FunctionCode == 3 || FunctionCode == 4)
            {
                ByteRegister = new ushort[RegisterQuantity];
                int j = 0;
                for (int i = 0; i < RegisterQuantity; i++)
                {
                    byte[] reg = new byte[2] { ResponseData[j + 1], ResponseData[j] };
                    ByteRegister[i] = BitConverter.ToUInt16(reg);
                    j += 2;
                }
            }
        }

        [Serializable]
        public class ModbusException : Exception
        {
            public ModbusException() : base() { }
            public ModbusException(string message) : base(message) { }

            // A constructor is needed for serialization when an
            // exception propagates from a remoting server to the client.
            protected ModbusException(System.Runtime.Serialization.SerializationInfo info,
                System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
        }

        public static void HandleException(ushort except)
        {
            switch (except)
            {
                case 1:
                    throw new ModbusException("Illegal Function: The function code received in the query is not an allowable action for the server.");
                case 2:
                    throw new ModbusException("Illegal Data Address: The data address received in the query is not an allowable address for the server.");
                case 3:
                    throw new ModbusException("Illegal Data Value: A value contained in the query data field is not an allowable value for the server.");
                case 4:
                    throw new ModbusException("Server Device Failure: An unrecoverable error occurred while the server was attempting to perform the requested action.");
                case 5:
                    throw new ModbusException("Acknowledge: The server has accepted the request and is processing it, but a long duration of time will be required to do so.");
                case 6:
                    throw new ModbusException("Server Device Busy: The server is engaged in processing a long-duration program command.");
                case 7:
                    throw new ModbusException("Negative Acknowledge: The server cannot perform the program function received in the query.");
                case 8:
                    throw new ModbusException("Memory Parity Error: The server attempted to read extended memory or record file, but detected a parity error in memory.");
                case 10:
                    throw new ModbusException("Gateway Path Unavailble: The gateway was unable to allocate an internal communication path from the input port to the output port for processing the request.");
                case 11:
                    throw new ModbusException("Gateway Target Device Failed to Respond: No response was obtained from the target device.");
                    
            }
        }


    }

    /// <summary>
    /// The MODBUS application data unit is built by the client that initiates a MODBUS transaction.
    /// </summary>
    class ApplicationDataUnit : ProtocolDataUnit
    {
        /// <summary>
        /// 2 bytes set by the Client to uniquely identify each request. These bytes are echoed by the Server since its responses may not be received in the same order as the requests.
        /// </summary>
        public ushort TransactionID { get; set; } = 1;

        /// <summary>
        /// 2 bytes set by the Client, always = 00 00
        /// </summary>
        private readonly ushort ProtocolID = 0;

        /// <summary>
        /// 2 bytes identifying the number of bytes in the message to follow. Unit ID + Function Code + Data
        /// </summary>
        public ushort RequestLength { get { return Convert.ToUInt16(2 + RequestData.Length); } }
        public ushort ResponseLength { get; set; }

        /// <summary>
        /// Assuming proper response, determine length of response for proper memory allocation of byte reponse length
        /// </summary>
        public ushort FullADUResponseLength
        {
            get
            {
                ushort aduHeaderLength = 8;

                if (FunctionCode == 1 || FunctionCode == 2)
                {
                    ushort byteRegisterLength = Convert.ToUInt16(RegisterQuantity / 8);

                    if (RegisterQuantity % 8 == 0) { return Convert.ToUInt16(aduHeaderLength + byteRegisterLength + 1); }
                    else { return Convert.ToUInt16(aduHeaderLength + byteRegisterLength + 2); };
                }
                else if(FunctionCode == 3 || FunctionCode == 4)
                {
                    return Convert.ToUInt16(aduHeaderLength + RegisterQuantity*2 + 1);
                }
                else { return 1; }
            }
        }

        /// <summary>
        /// 1 byte set by the Client and echoed by the Server for identification of a remote slave connected on a serial line or on other buses in hexadecimal
        /// </summary>
        public byte UnitID { get; set; }

        /// <summary>
        /// Build Modbus Application Header for Modbus TCP/IP  = Transactation + ProtocolIdentifier + Length + UnitID in BigEndian
        /// </summary>
        public byte[] MBAPHeader
        {
            get
            {
                Byte[] mbapHeader = new byte[]
                {
                    BitConverter.GetBytes((ushort)TransactionID)[1],
                    BitConverter.GetBytes((ushort)TransactionID)[0],
                    BitConverter.GetBytes((ushort)ProtocolID)[1],
                    BitConverter.GetBytes((ushort)ProtocolID)[0],
                    BitConverter.GetBytes((ushort)RequestLength)[1],
                    BitConverter.GetBytes((ushort)RequestLength)[0],
                    UnitID
                };

                return mbapHeader;
            }
        }

        /// <summary>
        /// Full Modbus TCP/IP Application Data Unit
        /// </summary>
        public byte[] Request
        {
            get
            {
                // combine components of mbap header + function code + data
                byte[] adu = new byte[MBAPHeader.Length + 1 + RequestData.Length];
                Buffer.BlockCopy(MBAPHeader, 0, adu, 0, MBAPHeader.Length);
                adu[MBAPHeader.Length] = FunctionCode;
                Buffer.BlockCopy(RequestData, 0, adu, MBAPHeader.Length + 1, RequestData.Length);

                return adu;
            }

        }

        /// <summary>
        /// Handle response to request
        /// </summary>
        /// <param name="aduResponse"></param>
        public void ParseAduResponse(byte[] aduResponse)
        {
            // check Transaction ID match
            if (TransactionID == BitConverter.ToUInt16(new byte[2] { aduResponse[1], aduResponse[0] }))
            {
                // check function code match
                ResponseFunctionCode = aduResponse[7];
                if (FunctionCode == ResponseFunctionCode)
                {
                    ResponseLength = BitConverter.ToUInt16(new byte[2] { aduResponse[5], aduResponse[4] });
                    ByteCount = aduResponse[8];

                    // get response data by removing MBAP Header
                    ResponseData = new byte[ByteCount];
                    Buffer.BlockCopy(aduResponse, 9, ResponseData, 0, ByteCount);

                    ParseResponse();
                    
                }
                else if(ResponseFunctionCode == (FunctionCode + 0x80)) { HandleException(aduResponse[8]); }
                
            }
            
            
            
        } 
    }
    

    class Program
    {
        public static TcpClient client = new();
        public static NetworkStream stream;

        public static void TcpConnect(string ipAddress, int port, int timeout)
        {         
            IAsyncResult con = client.BeginConnect(ipAddress, port, null, null);
            bool conSuccess = con.AsyncWaitHandle.WaitOne(timeout);

            if (conSuccess)
            {
                stream = client.GetStream();
                stream.ReadTimeout = timeout;
            }

        }
        
        public static void TcpDisconnect()
        {
            if (stream != null)
            {
                stream.Close();
            }
            if (client != null)
            {
                client.Close();
                client.Dispose();
            }
            
        }

        public static bool[] ReadCoils(ushort trasactionId, byte serverId, ushort startingAddr, ushort registerQty)
        {
            // set function
            ApplicationDataUnit applicationDataUnit = new()
            {
                TransactionID = trasactionId,
                UnitID = serverId,
                FunctionCode = 1,
                StartingAddress = startingAddr,
                RegisterQuantity = registerQty
            };

            // get formatted Modbus TCP request 
            byte[] adu = applicationDataUnit.Request;
            //Console.WriteLine(BitConverter.ToString(adu));

            // send requst to server
            stream.Write(adu, 0, adu.Length);

            //Read NetworkStream response a byte buffer
            byte[] response = new byte[applicationDataUnit.FullADUResponseLength];
            stream.Read(response, 0, response.Length);

            // parse response
            applicationDataUnit.ParseAduResponse(response);

            return applicationDataUnit.BoolRegister;

        }


        static void Main() //string[] args
        {
            // connect to Modbus server
            TcpConnect("127.0.0.4", 502, 1000);

            bool[] coils = ReadCoils(123, 1, 0, 8);

            // report out
            foreach (bool c in coils) { Console.WriteLine(c); }
            //foreach(ushort u in applicationDataUnit.ByteRegister) { Console.WriteLine(u); }


            // Disconnect
            TcpDisconnect();



        }


    }
}
