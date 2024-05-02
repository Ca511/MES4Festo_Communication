using System.Collections.Specialized;
using System.Net.Sockets;
using System.Xml;
using System.Xml.Linq;

namespace ClassLibNETStand_MES4FestoConnector
{

    /// <summary>
    /// Represents a connector to communicate with the MES4 system of Festo. 
    /// This class provides functionalities to connect, send and receive messages, and handle service packages.
    /// </summary>
    public class MES4FestoConnector
    {

        #region Fields

        // TCP clients for status and service communication
        private TcpClient? statusTCPClient;
        private NetworkStream? statusStream;

        private TcpClient? serviceTCPClient;
        private NetworkStream? serviceStream;

        // MES host and port information
        private readonly string MES4Host;
        private readonly int MES4StatusPort;
        private readonly int MES4ServicePort;

        // Interval for sending status messages in milliseconds
        private const int Interval = 1000;

        // Resource information
        private readonly UInt16 ResourceID;
        private readonly PLCType ResourcePLCType;
        private readonly bool IsResource;

        // Thread for sending status messages
        private Thread? StatusMessageThread;
        private Status _ResourceStatus;

        // Header dictionaries for GET and SEND requests
        private readonly OrderedDictionary HeaderGetDic;
        private readonly OrderedDictionary HeaderSendDic;

        // Flag to track disposal status
        private bool disposed = false;

        #endregion


        #region Constructors

        /// <summary>
        /// Initializes a new instance of the MES4FestoConnector class.
        /// </summary>
        /// <param name="mESHost">The MES4 host address.</param>
        /// <param name="resourceID">The resource ID for this connection.</param>
        /// <param name="resourcePLCType">The type of PLC used by the resource. Siemens or CodeSys.</param>
        /// <param name="isResource">Indicates whether this instance represents a resource.</param>
        /// <param name="resourceStatus">The initial status of the resource.</param>
        /// <param name="mESStatusPort">The port used for MES4 status communication. Default is 2001.</param>
        /// <param name="mESServicePort">The port used for MES4 service communication. Default is 2000.</param>
        /// <exception cref="ArgumentException">Thrown when the MES4 host address is null or empty.</exception>
        /// <exception cref="ArgumentNullException">Thrown when the resource status is null.</exception>
        /// <exception cref="HeaderInterpretationException">Thrown when there is a failure in interpreting the XML headers for the communication.</exception>
        /// <exception cref="InvalidOperationException">Thrown when there is an unexpected error during the initialization of the header, or when XML header interpretation fails.</exception>
        public MES4FestoConnector(string mESHost, UInt16 resourceID, PLCType resourcePLCType, bool isResource, ref Status resourceStatus, int mESStatusPort = 2001, int mESServicePort = 2000)
        {
            if (string.IsNullOrEmpty(mESHost))
                throw new ArgumentException("Host address cannot be null or empty.", nameof(mESHost));

            MES4Host = mESHost;
            MES4StatusPort = mESStatusPort;
            MES4ServicePort = mESServicePort;
            ResourceID = isResource ? resourceID : (UInt16)0;
            ResourcePLCType = resourcePLCType;
            IsResource = isResource;
            _ResourceStatus = resourceStatus ?? throw new ArgumentNullException(nameof(resourceStatus));


            // Header intialization

            try
            {
                HeaderGetDic = InterpretXMLHeader(@"MES_config\HeaderGet.xml");
                HeaderSendDic = InterpretXMLHeader(@"MES_config\HeaderSend.xml");
            }
            catch (HeaderInterpretationException ex)
            {
                throw new InvalidOperationException("Failed to interpret XML headers.", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("An unexpected error occurred during header initialization.", ex);
            }
        }


        #endregion


        #region Properties

        /// <summary>
        /// Gets or sets the resource status.
        /// </summary>
        public Status ResourceStatus
        {
            get => _ResourceStatus;
            private set
            {
                if (!Equals(_ResourceStatus, value))
                {
                    _ResourceStatus = value;
                    ResourceStatusChanged?.Invoke(this, new ResourceStatusChangedEventArgs(_ResourceStatus));
                }
            }
        }

        public UInt16 Resource_ID { get { return ResourceID; } } 


        #endregion


        #region Enumerations

        /// <summary>
        /// Defines the types of PLC (Programmable Logic Controller) used.
        /// LittleEndian = CodeSys
        /// BigEndian = Siemens
        /// </summary>
        public enum PLCType
        {
            LittleEndian,
            BigEndian
        }


        #endregion


        #region Subclasses

        /// <summary>
        /// Represents a header data and standard parameter structure used in service calls.
        /// </summary>
        class HeaderData
        {
            // Properties of the HeaderData class
            public int ID { get; private set; }
            public string ParameterName { get; private set; }
            public int Type { get; private set; }
            public int StringLength { get; private set; }
            public int CoDeSysV3_Address { get; private set; }

            /// <summary>
            /// Initializes a new instance of the HeaderData class.
            /// </summary>
            /// <param name="ID">The identifier of the header.</param>
            /// <param name="ParameterName">The name of the parameter.</param>
            /// <param name="Type">The data type of the parameter.</param>
            /// <param name="StringLength">The length of the string, if the parameter is a string.</param>
            /// <param name="CoDeSysV3Address">The address in the CoDeSys V3 system.</param>
            public HeaderData(int iD, string parameterName, int type, int stringLength, int coDeSysV3_Address)
            {
                ID = iD;
                ParameterName = parameterName;
                Type = type;
                StringLength = stringLength;
                CoDeSysV3_Address = coDeSysV3_Address;
            }

            /// <summary>
            /// Gets the C# equivalent type for the MES4 parameter type.
            /// </summary>
            /// <returns>The C# type equivalent to the MES4 parameter type.</returns>
            /// <exception cref="ArgumentException">
            /// Thrown when the Type is not supported.
            /// This method currently supports the following Types:
            /// 1 for Int16, 2 for Int32, and 3 for string.
            /// Any other Type value will cause this exception to be thrown.
            /// </exception>
            internal Type GetCSType()
            {
                return Type switch
                {
                    1 => typeof(Int16),
                    2 => typeof(Int32),
                    3 => typeof(string),
                    _ => throw new ArgumentException($"Unsupported type: {Type}")
                };
            }
        }

        /// <summary>
        /// Represents the status of the resource, including modes and error flags.
        /// The status will be coded into a byte array and transferred every 1000 millisecons to the MES4 when given as ref parameter to the MES4FestoConnector Constructor.
        /// </summary>
        public class Status
        {
            /// <summary>
            /// Initializes a new instance of the Status class.
            /// </summary>
            /// <param name="resourceMode">The mode of the resource. Default is AutoMode.</param>
            /// <param name="busy">Indicates whether the resource is busy. Default is false.</param>
            /// <param name="reset">Indicates whether the resource needs to be reset. Default is false.</param>
            /// <param name="mESMode">Indicates whether the resource is in MES mode. Default is true.</param>
            /// <remarks>
            /// The Status class represents the state of a resource, including its mode, busy state, reset state, and MES mode.
            /// </remarks>
            public Status(Status.Mode resourceMode = Status.Mode.AutoMode, bool busy = false, bool reset = false, bool mESMode = true)
            {
                ResourceMode = resourceMode;
                Busy = busy;
                Reset = reset;
                MESMode = mESMode;
            }


            // Definitions and properties of the Status class
            /// <summary>
            /// Defines the modes of operation for a resource.
            /// </summary>
            public enum Mode
            {
                /// <summary>
                /// Indicates the resource is operating in automatic mode.
                /// </summary>
                AutoMode,

                /// <summary>
                /// Indicates the resource is operating in manual mode.
                /// </summary>
                ManualMode
            }

            /// <summary>
            /// Gets or sets the current mode of the resource.
            /// </summary>
            public Mode ResourceMode { get; private set; }

            /// <summary>
            /// Gets or sets a value indicating whether the resource is currently busy.
            /// </summary>
            public bool Busy { get; private set; }

            /// <summary>
            /// Gets or sets a value indicating whether the resource needs to be reset.
            /// </summary>
            public bool Reset { get; private set; }

            /// <summary>
            /// An array representing error flags for the resource. Each element in the array indicates a specific type of error.
            /// There are 3 bool values to be set as error flags.
            /// </summary>
            public bool[] ErrorFlags { get; private set; } = new bool[3] { false, false, false };

            /// <summary>
            /// Gets or sets a value indicating whether the resource is in MES4 (Manufacturing Execution System) mode.
            /// </summary>
            public bool MESMode { get; private set; }



            /// <summary>
            /// Switches the mode of the resource to the specified mode.
            /// </summary>
            /// <param name="newResourceMode">The new mode to switch to.</param>
            public void SwitchMode(Mode newResourceMode) { ResourceMode = newResourceMode; }

            /// <summary>
            /// Sets the busy state of the resource.
            /// </summary>
            /// <param name="busy">True to indicate the resource is busy; false otherwise.</param>

            public void SetBusyBit(bool busy) { Busy = busy; }

            /// <summary>
            /// Sets the reset state of the resource.
            /// </summary>
            /// <param name="reset">True to indicate the resource should be reset; false otherwise.</param>
            public void SetResetBit(bool reset) { Reset = reset; }

            /// <summary>
            /// Sets a specific error flag for the resource.
            /// </summary>
            /// <param name="error">The error state to set.</param>
            /// <param name="flagNumber">The number of the error flag to set (0, 1, or 2).</param>
            public void SetErrorFlagBit(bool error, byte flagNumber)
            {
                if (flagNumber > 2)
                    throw new Exception("Errorflagnumbers can only be 0, 1 or 2");

                ErrorFlags[flagNumber] = error;
            }

            /// <summary>
            /// Sets the MES mode of the resource.
            /// </summary>
            /// <param name="mesMode">True to indicate the resource is in MES mode; false otherwise.</param>
            public void SetMESMode(bool mesMode) { MESMode = mesMode; }

            /// <summary>
            /// Constructs a byte representation of the current status.
            /// </summary>
            /// <returns>A byte representing the current status of the resource.</returns>
            public byte CreateStatusByteMessage()
            {
                byte statusByte = 0b0000_0000;

                if (ResourceMode == Mode.AutoMode)
                    statusByte |= (1 << 0);
                else
                    statusByte |= (1 << 1);

                if (Busy)
                    statusByte |= (1 << 2);

                if (Reset)
                    statusByte |= (1 << 3);

                for (int i = 0; i < ErrorFlags.Length; i++)
                {
                    if (ErrorFlags[i])
                        statusByte |= (byte)(1 << (i + 4));
                }

                if (MESMode)
                    statusByte |= (1 << 7);

                return statusByte;
            }

        }

        /// <summary>
        /// Represents a service package for communication with the MES4 system.
        /// </summary>
        public class ServicePackage
        {
            // Fields and Properties

            /// <summary>
            /// The header dictionary for GET requests.
            /// </summary>
            protected readonly OrderedDictionary HeaderGetDic;
            /// <summary>
            /// The header dictionary for SEND responses.
            /// </summary>
            protected readonly OrderedDictionary HeaderSendDic;


            /// <summary>
            /// The PLC type of the sender resource.
            /// </summary>
            protected PLCType SenderResourcePLCType { get; }
            /// <summary>
            /// Indicates whether the sender is a resource.
            /// </summary>
            protected bool SenderIsResource { get; }
            /// <summary>
            /// The request ID of the sender.
            /// </summary>
            protected Int16 SenderRequestID { get; }


            // Service Parameters
            /// <summary>
            /// Gets the message class identifier for the service package.
            /// </summary>
            /// <value>The message class identifier, represented as an Int16.</value>
            public Int16 MClass { get; }

            /// <summary>
            /// Gets the message number for the service package.
            /// </summary>
            /// <value>The message number, represented as an Int16.</value>
            public Int16 MNo { get; }

            /// <summary>
            /// Gets the error state for the service package.
            /// </summary>
            /// <value>The error state, represented as an Int16, where 0 means that there is no error</value>
            public Int16 ErrorState { get; }

            /// <summary>
            /// Gets the dictionary of standard parameters associated with the service package.
            /// </summary>
            /// <value>A dictionary of standard parameters, where each parameter is represented as a key-value pair with the key as a string and the value as an object.</value>
            public Dictionary<string, object> StandardParameters { get; }

            /// <summary>
            /// Gets the dictionary of service-specific parameters associated with the service package.
            /// </summary>
            /// <value>A dictionary of service-specific parameters, where each parameter is represented as a key-value pair with the key as a string and the value as an object.</value>
            public Dictionary<string, object> ServiceSpecificParameters { get; }



            // Constructor for Request

            /// <summary>
            /// Initializes a new instance of the ServicePackage class for sending a request.
            /// This constructor creates a service package for communication with the MES4 system.
            /// </summary>
            /// <param name="Connector">The MES4FestoConnector instance associated with this package.</param>
            /// <param name="MClass">The class of the message, represented as an Int16.</param>
            /// <param name="MNo">The message number, represented as an Int16.</param>
            /// <param name="ErrorState">The error state of the message, represented as an Int16.</param>
            /// <param name="StandardParameters">A dictionary containing the standard parameters for the message. Each parameter should be either an Int16, Int32, or string.</param>
            /// <param name="ServiceSpecificParameters">A dictionary containing the service-specific parameters for the message. Each parameter should be either an Int16, Int32, or string.</param>
            /// <exception cref="ArgumentNullException">Thrown when the 'Connector' parameter is null.</exception>
            /// <exception cref="ArgumentException">Thrown when 'StandardParameters' or 'ServiceSpecificParameters' contain types other than Int16, Int32, or string.</exception>
            public ServicePackage(MES4FestoConnector Connector, Int16 MClass, Int16 MNo, Int16 ErrorState, Dictionary<string, object> StandardParameters, Dictionary<string, object> ServiceSpecificParameters)
            {
                if (Connector is null) throw new ArgumentNullException(nameof(Connector));

                foreach (var standardParameter in StandardParameters.Values)
                    if (standardParameter is not Int16 && standardParameter is not Int32 && standardParameter is not string)
                        throw new ArgumentException("MES4FestoConnector class can only handle Int16, Int32 and string values as input StandardParameters");


                foreach (var serviceSpecificParameter in StandardParameters.Values)
                    if (serviceSpecificParameter is not Int16 && serviceSpecificParameter is not Int32 && serviceSpecificParameter is not string)
                        throw new ArgumentException("MES4FestoConnector class can only handle Int16, Int32 and string values as input ServiceSpecificParameters");


                this.HeaderGetDic = Connector.HeaderGetDic;
                this.HeaderSendDic = Connector.HeaderSendDic;

                this.SenderResourcePLCType = Connector.ResourcePLCType;
                this.SenderIsResource = Connector.IsResource;
                this.SenderRequestID = Convert.ToInt16(Connector.ResourceID);
                this.MClass = MClass;
                this.MNo = MNo;
                this.ErrorState = ErrorState;
                this.StandardParameters = StandardParameters;
                this.ServiceSpecificParameters = ServiceSpecificParameters;
            }


            // Constructor for Response

            /// <summary>
            /// Initializes a new instance of the ServicePackage class for receiving a response.
            /// This constructor parses a data string to extract service package information.
            /// </summary>
            /// <param name="Connector">The MES4FestoConnector instance associated with this package.</param>
            /// <param name="DataString">The raw data string representing the response from the MES4 system.</param>
            /// <exception cref="ArgumentNullException">Thrown when the 'Connector' parameter is null.</exception>
            /// <exception cref="ArgumentException">Thrown when the input data string is null, empty, or contains duplicate keys.</exception>
            /// <exception cref="FormatException">Thrown when the data string has an invalid key-value pair format.</exception>
            internal ServicePackage(MES4FestoConnector Connector, string DataString)
            {
                if (Connector is null) throw new ArgumentNullException(nameof(Connector));

                this.HeaderGetDic = Connector.HeaderGetDic;
                this.HeaderSendDic = Connector.HeaderSendDic;

                this.SenderResourcePLCType = Connector.ResourcePLCType;
                this.SenderIsResource = Connector.IsResource;
                this.SenderRequestID = Convert.ToInt16(Connector.ResourceID);

                this.StandardParameters = [];
                this.ServiceSpecificParameters = [];


                try
                {

                    Dictionary<string, object> ExtractedParameterStrings = ExtractDataLongEncoding(DataString);

                    foreach (var eps in ExtractedParameterStrings)
                    {
                        Type type = GetTypeOfParameter(eps);

                        if (HeaderGetDic.Contains(eps.Key))
                            StandardParameters.Add(eps.Key, Convert.ChangeType(eps.Value, type));
                        else
                            ServiceSpecificParameters.Add(eps.Key, eps.Value);
                    }

                }
                catch (Exception)
                {
                    throw;
                }



                Dictionary<string, object> ExtractDataLongEncoding(string input)
                {
                    input.Remove(0, 4);

                    var dataDictionary = new Dictionary<string, object>();

                    if (string.IsNullOrEmpty(input))
                    {
                        throw new ArgumentException("Input string for data extraction is null or empty.");
                    }

                    input = input.Replace("\\r", "");

                    var pairs = input.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (var pair in pairs)
                    {
                        var keyValue = pair.Split(new[] { '=' }, StringSplitOptions.RemoveEmptyEntries);

                        if (keyValue.Length != 2)
                        {
                            throw new FormatException($"Invalid key-value pair format: '{pair}' in input string.");
                        }

                        string key = keyValue[0].Trim();
                        object value = keyValue[1].Trim();  // Weiterführende Validierung oder Parsing kann hier hinzugefügt werden.

                        if (!dataDictionary.TryAdd(key, value))
                        {
                            throw new ArgumentException($"Duplicate key '{key}' encountered in input string.");
                        }
                    }

                    return dataDictionary;
                }



                Type GetTypeOfParameter(KeyValuePair<string, object> kvp)
                {
                    if (HeaderGetDic.Contains(kvp.Key))
                    {
                        HeaderData headerData = HeaderGetDic[kvp.Key] as HeaderData ?? throw new ArgumentNullException(kvp.Key.ToString(), "Could not find Value-Key in HeaderGet");

                        if (headerData is not null)
                            return headerData.Type switch
                            {
                                1 => typeof(Int16),
                                2 => typeof(Int32),
                                3 => typeof(string),
                                _ => throw new ArgumentException("Headerdata-XML contained a Type different to 1, 2 or 3", headerData.ToString()),
                            };
                        else
                        {
                            throw new Exception();
                        }
                    }
                    else
                        return typeof(object);

                }

                Type GetTypeOfParameter(short type_identifer)
                {
                        switch (value)
                        {
                            case 1:
                                return typeof(Int16);
                            case 2:
                                return typeof(Int32);
                            case 3:
                                return typeof(string);
                            default:
                                throw new ArgumentException("Incorrect value. It must be 1, 2 or 3.");
                        }
                }
            }


            // Methods

            /// <summary>
            /// Creates a string representation of the service request for communication.
            /// This method constructs a string that encapsulates all the necessary details for a service request,
            /// including the message class, message number, error state, and other parameters.
            /// </summary>
            /// <param name="Connector_sender">The sender's MES4FestoConnector instance.</param>
            /// <returns>A string representing the service request, formatted for communication with the MES4 system.</returns>
            /// <exception cref="ArgumentNullException">Thrown when the 'Connector_sender' parameter is null.</exception>
            /// <exception cref="ArgumentException">Thrown when any of the values in 'StandardParameters' are not of type Int16, Int32, or string.</exception>
            public string CreateServiceRequestString(MES4FestoConnector Connector_sender)
            {
                if (Connector_sender is null) throw new ArgumentNullException(nameof(Connector_sender));


                string ServiceCallString;

                // TcpIdent
                ServiceCallString = "444;";

                // RequestID
                if (Connector_sender.IsResource)
                    ServiceCallString += "RequestId=" + Connector_sender.ResourceID + ";";

                // MClass
                ServiceCallString += "MClass=" + this.MClass.ToString() + ";";

                // MNo
                ServiceCallString += "MNo=" + this.MNo.ToString() + ";";

                // ErrorState
                ServiceCallString += "ErrorState=" + this.ErrorState.ToString() + ";";


                foreach (var inputParam in this.StandardParameters)
                {
                    string paramName = inputParam.Key;

                    if (!paramName.StartsWith('#'))
                        paramName = "#" + paramName;


                    if (inputParam.Value is Int16 || inputParam.Value is Int32 || inputParam.Value is string)
                        ServiceCallString += $"{paramName}={inputParam.Value};";
                    else throw new ArgumentException("Service Request Strings can only be created for Int16, Int 32, or sting values", inputParam.ToString());
                }


                if (ServiceCallString[^1] == ';')
                    ServiceCallString = ServiceCallString[..^1];

                ServiceCallString += "*";


                return ServiceCallString;
            }

        }


        #endregion


        #region Events


        /// <summary>
        /// Occurs when the connection is disconnected.
        /// </summary>
        public event EventHandler? Disconnected;

        /// <summary>
        /// Occurs when a status message is successfully sent.
        /// </summary>
        public event EventHandler<StatusMessageSentEventArgs>? StatusMessageSent;

        /// <summary>
        /// Occurs when there is a failure in transferring a status message.
        /// </summary>
        public event EventHandler<StatusMessageTransferFailedEventArgs>? StatusMessageTransferFailed;

        /// <summary>
        /// Occurs when the resource status changes.
        /// </summary>
        public event EventHandler<ResourceStatusChangedEventArgs>? ResourceStatusChanged;

        /// <summary>
        /// Occurs when a TCP connection is successfully established.
        /// </summary>
        public event EventHandler<TCPConnectedEventArgs>? Connected;

        /// <summary>
        /// Occurs when a service request is sent.
        /// </summary>
        public event EventHandler<ServiceRequestSentEventArgs>? ServiceRequestSent;

        /// <summary>
        /// Occurs when a service is called.
        /// </summary>
        public event EventHandler<ServiceCalledEventArgs>? ServiceCalled;

        /// <summary>
        /// Occurs when there is a failure in calling a service.
        /// </summary>
        public event EventHandler<ServiceCallFailedEventArgs>? ServiceCallFailed;

        #endregion


        #region EventArguments

        /// <summary>
        /// Provides data for the StatusMessageSent event.
        /// </summary>
        public class StatusMessageSentEventArgs(byte[] Message) : EventArgs
        {
            private readonly byte[] _Message = Message;

            public byte[] Message { get { return _Message; } }
        }

        /// <summary>
        /// Provides data for the StatusMessageTransferFailed event.
        /// </summary>
        public class StatusMessageTransferFailedEventArgs(string ErrorDiscription)
        {
            private readonly string _ErrorDiscription = ErrorDiscription;

            public string ErrorDiscription { get { return _ErrorDiscription; } }
        }

        /// <summary>
        /// Provides data for the ResourceStatusChanged event.
        /// </summary>
        public class ResourceStatusChangedEventArgs(Status Status) : EventArgs
        {
            private readonly Status _Status = Status;
            public Status MyProperty { get { return _Status; } }
        }

        /// <summary>
        /// Provides data for the TCPConnected event.
        /// </summary>
        public class TCPConnectedEventArgs(string Host, int StatusPort, int ServicePort)
        {
            private readonly string _Host = Host;
            public string Host { get { return _Host; } }


            private readonly int _StatusPort = StatusPort;
            public int StatusPort { get { return _StatusPort; } }


            private readonly int _ServicePort = ServicePort;
            public int ServicePort { get { return _ServicePort; } }
        }

        /// <summary>
        /// Provides data for the ServiceRequestSent event.
        /// </summary>
        public class ServiceRequestSentEventArgs(ServicePackage ServicePackage)
        {
            private readonly ServicePackage _ServicePackage = ServicePackage;
            public ServicePackage ServicePackage { get { return _ServicePackage; } }
        }

        /// <summary>
        /// Provides data for the ServiceCalled event.
        /// </summary>
        public class ServiceCalledEventArgs(ServicePackage SentServicePackage, ServicePackage ReceivedServicePackage)
        {
            private readonly ServicePackage _SentServicePackage = SentServicePackage;
            public ServicePackage SentServicePackage { get { return _SentServicePackage; } }


            private readonly ServicePackage _ReceivedServicePackage = ReceivedServicePackage;
            public ServicePackage ReceivedServicePackage { get { return _ReceivedServicePackage; } }
        }

        /// <summary>
        /// Provides data for the ServiceCallFailed event.
        /// </summary>
        public class ServiceCallFailedEventArgs(string ErrorDiscription)
        {
            private readonly string _ErrorDiscription = ErrorDiscription;

            public string ErrorDiscription { get { return _ErrorDiscription; } }
        }

        #endregion


        #region Exceptions

        /// <summary>
        /// Represents an exception related to header interpretation in the MES4FestoConnector class.
        /// </summary>
        [Serializable]
        internal class HeaderInterpretationException : Exception
        {
            public HeaderInterpretationException()
            {
            }

            public HeaderInterpretationException(string? message) : base(message)
            {
            }

            public HeaderInterpretationException(string? message, Exception? innerException) : base(message, innerException)
            {
            }
        }

        /// <summary>
        /// Represents an exception that occurs during service calls in the MES4FestoConnector class.
        /// </summary>
        [Serializable]
        internal class ServiceCallFailedException : Exception
        {
            public ServiceCallFailedException()
            {
            }

            public ServiceCallFailedException(string? message) : base(message)
            {
            }

            public ServiceCallFailedException(string? message, Exception? innerException) : base(message, innerException)
            {
            }
        }

        /// <summary>
        /// Represents an exception that occurs during disconnection in the MES4FestoConnector class.
        /// </summary>
        [Serializable]
        internal class DisconnectException : Exception
        {
            public DisconnectException()
            {
            }

            public DisconnectException(string? message) : base(message)
            {
            }

            public DisconnectException(string? message, Exception? innerException) : base(message, innerException)
            {
            }
        }

        /// <summary>
        /// Represents an exception that occurs during reconnection in the MES4FestoConnector class.
        /// </summary>
        [Serializable]
        internal class ReconnectException : Exception
        {
            public ReconnectException()
            {
            }

            public ReconnectException(string? message) : base(message)
            {
            }

            public ReconnectException(string? message, Exception? innerException) : base(message, innerException)
            {
            }
        }


        #endregion


        #region Methods

        /// <summary>
        /// Establishes connections to the MES4 system for status and service communication.
        /// </summary>
        /// <exception cref="SocketException">Thrown when a socket error occurs during the connection process.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs during the connection process.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the operation is performed on a disposed object.</exception>
        /// <exception cref="InvalidOperationException">Thrown when an invalid operation occurs during the connection process.</exception>
        public void Connect()
        {
            try
            {
                EstablishNewConnections();

                StartSendingStatusMessages();

                Connected?.Invoke(this, new TCPConnectedEventArgs(MES4Host, MES4StatusPort, MES4ServicePort));
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"Socket Error: {ex.Message}");
                // Optional: Rethrow the exception or handle it as required
                throw;
            }
            catch (IOException ex)
            {
                Console.WriteLine($"I/O Error: {ex.Message}");
                // Optional: Rethrow the exception or handle it as required
                throw;
            }
            catch (ObjectDisposedException ex)
            {
                Console.WriteLine($"Object Disposed Error: {ex.Message}");
                // Optional: Rethrow the exception or handle it as required
                throw;
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"Invalid Operation: {ex.Message}");
                // Optional: Rethrow the exception or handle it as required
                throw;
            }
            // Weitere spezifische Exceptions hier behandeln.
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.Message}");
                // Optional: Rethrow the exception or handle it as required
                throw;
            }
        }

        /// <summary>
        /// Disconnects from the MES4 system and cleans up resources.
        /// </summary>
        /// <exception cref="DisconnectException">Thrown when there is an issue stopping the StatusMessageThread or closing the TCP client or stream.</exception>
        public void Disconnect()
        {
            StopStatusMessageThread();

            CloseTcpClient(ref statusTCPClient);
            CloseStream(ref statusStream);

            CloseTcpClient(ref serviceTCPClient);
            CloseStream(ref serviceStream);

            Disconnected?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Initiates the process of sending status messages in a separate thread.
        /// </summary>
        private void StartSendingStatusMessages()
        {
            StatusMessageThread = new Thread(SendStatusMessagesLoop);
            StatusMessageThread.Start();
        }

        /// <summary>
        /// Handles the continuous sending of status messages in a loop.
        /// This method is designed to run in a separate thread and continuously sends status messages at regular intervals.
        /// </summary>
        /// <remarks>
        /// The loop continues until the object is disposed. In each iteration, the method ensures the connection is active,
        /// sends a status message, and then pauses for a defined interval.
        /// </remarks>
        /// <exception cref="ThreadInterruptedException">Thrown when the thread executing the loop is interrupted, typically during shutdown or disposal of the object.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the connection cannot be ensured. This could be due to connection issues.</exception>
        private void SendStatusMessagesLoop()
        {
            while (!disposed)
            {
                try
                {
                    EnsureConnection();
                    SendStatusMessage();
                    Thread.Sleep(Interval);
                }
                catch (ThreadInterruptedException)
                {
                    // Der Thread wurde unterbrochen, zum Beispiel während des Herunterfahrens
                    break;
                }
                catch (Exception ex)
                {
                    StatusMessageTransferFailed?.Invoke(this, new StatusMessageTransferFailedEventArgs(ex.Message));
                    Reconnect();
                }
            }
        }

        /// <summary>
        /// Stops the thread responsible for sending status messages to the MES4 system.
        /// This method is designed to safely terminate the StatusMessageThread, ensuring that it ceases its operations
        /// and that all associated resources are properly released.
        /// </summary>
        /// <exception cref="DisconnectException">Thrown when there is an issue stopping the StatusMessageThread, such as a thread interruption.</exception>
        private void StopStatusMessageThread()
        {
            if (StatusMessageThread != null && StatusMessageThread.IsAlive)
            {
                try
                {
                    StatusMessageThread.Interrupt();
                    StatusMessageThread.Join();
                }
                catch (ThreadInterruptedException ex)
                {
                    throw new DisconnectException("Failed to stop the StatusMessageThread.", ex);
                }
                finally
                {
                    StatusMessageThread = null;
                }
            }
        }

        /// <summary>
        /// Closes a given TCP client and ensures all associated resources are released.
        /// </summary>
        /// <param name="tcpClient">The TCP client to be closed.</param>
        /// <exception cref="DisconnectException">Thrown when there is an issue closing the TCP client.</exception>
        private void CloseTcpClient(ref TcpClient? tcpClient)
        {
            if (tcpClient != null)
            {
                try
                {
                    tcpClient.Close();
                }
                catch (SocketException ex)
                {
                    throw new DisconnectException("Failed to close TCP client.", ex);
                }
                finally
                {
                    tcpClient = null;
                }
            }
        }

        /// <summary>
        /// Closes a given network stream and ensures all associated resources are released.
        /// </summary>
        /// <param name="stream">The network stream to be closed.</param>
        /// <exception cref="DisconnectException">Thrown when there is an issue closing the network stream.</exception>
        private void CloseStream(ref NetworkStream? stream)
        {
            if (stream != null)
            {
                try
                {
                    stream.Close();
                }
                catch (IOException ex)
                {
                    throw new DisconnectException("Failed to close network stream.", ex);
                }
                finally
                {
                    stream = null;
                }
            }
        }

        /// <summary>
        /// Sends a service request to the MES4 system.
        /// </summary>
        /// <param name="request_servicePackage">The service package containing the request details.</param>
        /// <returns>A new ServicePackage containing the response from the MES4 system.</returns>
        /// <exception cref="ServiceCallFailedException">Thrown when there is an issue with the TCP connection/stream or during the service call process.</exception>
        public void CallService(ServicePackage request_servicePackage, out ServicePackage response_servicePackage)
        {
            if (serviceStream is null || serviceTCPClient is null || !serviceTCPClient.Connected)
                Reconnect();

            if (serviceStream is null || serviceTCPClient is null || !serviceTCPClient.Connected)
            {
                throw new ServiceCallFailedException("Service call failed due to TCP connection/stream issues.");
            }

            string serviceCallString = request_servicePackage.CreateServiceRequestString(this);
            string receivedString;

            try
            {
                SendServiceRequest(serviceCallString);
                ServiceRequestSent?.Invoke(this, new ServiceRequestSentEventArgs(request_servicePackage));

                receivedString = ReceiveServiceResponse();
            }
            catch (IOException ex)
            {
                throw new ServiceCallFailedException("I/O error during service call.", ex);
            }
            catch (SocketException ex)
            {
                throw new ServiceCallFailedException("Socket error during service call.", ex);
            }
            catch (ObjectDisposedException ex)
            {
                throw new ServiceCallFailedException("Attempted to use a disposed object during service call.", ex);
            }
            // Catch other specific exceptions as necessary
            catch (Exception ex)
            {
                throw new ServiceCallFailedException("An unexpected error occurred during the service call.", ex);
            }

            response_servicePackage = new ServicePackage(this, receivedString);
        }

        /// <summary>
        /// Sends a service request to the MES4 system using the established TCP connection.
        /// </summary>
        /// <param name="serviceCallString">The service call string representing the request to be sent.</param>
        /// <exception cref="InvalidOperationException">Thrown when the service stream is not available or the TCP client is not connected.</exception>
        /// <exception cref="ServiceCallFailedException">Thrown when there is an issue with sending the service request.</exception>
        private void SendServiceRequest(string serviceCallString)
        {
            if (serviceStream is null || serviceTCPClient is null || !serviceTCPClient.Connected)
            {
                throw new InvalidOperationException("Service stream is not available or TCP client is not connected.");
            }

            try
            {
                byte[] byteData = System.Text.Encoding.ASCII.GetBytes(serviceCallString);
                serviceStream.Write(byteData, 0, byteData.Length);
            }
            catch (IOException ex)
            {
                throw new ServiceCallFailedException("Failed to send service request due to an I/O error.", ex);
            }
            catch (ObjectDisposedException ex)
            {
                throw new ServiceCallFailedException("Attempted to use a disposed object while sending service request.", ex);
            }
            // Catch other specific exceptions as necessary
        }

        /// <summary>
        /// Receives a service response from the MES4 system over the established TCP connection.
        /// </summary>
        /// <returns>A string containing the response from the MES4 system.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the service stream is not available or the TCP client is not connected.</exception>
        /// <exception cref="ServiceCallFailedException">Thrown when there is an issue with receiving the service response.</exception>
        private string ReceiveServiceResponse()
        {
            if (serviceStream is null || serviceTCPClient is null || !serviceTCPClient.Connected)
            {
                throw new InvalidOperationException("Service stream is not available or TCP client is not connected.");
            }

            try
            {
                byte[] buffer = new byte[1024];
                int bytesRead = serviceStream.Read(buffer, 0, buffer.Length);
                return System.Text.Encoding.ASCII.GetString(buffer, 0, bytesRead);
            }
            catch (IOException ex)
            {
                throw new ServiceCallFailedException("Failed to receive service response due to an I/O error.", ex);
            }
            catch (ObjectDisposedException ex)
            {
                throw new ServiceCallFailedException("Attempted to use a disposed object while receiving service response.", ex);
            }
            // Catch other specific exceptions as necessary
        }

        /// <summary>
        /// Ensures that the TCP connection for sending status messages is established and valid.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the connection is not established or valid for sending status messages.</exception>
        private void EnsureConnection()
        {
            if (statusTCPClient is null || !statusTCPClient.Connected || statusStream is null)
            {
                throw new InvalidOperationException("Connection is not established for sending status messages.");
            }
        }


        /// <summary>
        /// Sends a status message to the MES4 system to inform it of the current status of the resource.
        /// </summary>
        /// <exception cref="StatusMessageTransferFailedEventArgs">Triggered when there is an issue in sending the status message.</exception>
        private void SendStatusMessage()
        {
            if (statusTCPClient is null || !statusTCPClient.Connected || statusStream is null)
            {
                StatusMessageTransferFailed?.Invoke(this, new StatusMessageTransferFailedEventArgs("Failed to open TCP connection and streams."));
                Reconnect();
                return;
            }

            byte[] message = CreateStatusMessage();

            try
            {
                lock (statusStream)
                {
                    statusStream.Write(message, 0, message.Length);
                }
                StatusMessageSent?.Invoke(this, new StatusMessageSentEventArgs(message));
            }
            catch (IOException ex)
            {
                StatusMessageTransferFailed?.Invoke(this, new StatusMessageTransferFailedEventArgs("I/O error while sending status message: " + ex.Message));
            }
            catch (ObjectDisposedException ex)
            {
                StatusMessageTransferFailed?.Invoke(this, new StatusMessageTransferFailedEventArgs("Attempted to use a disposed object while sending status message: " + ex.Message));
            }
            // Weitere spezifische Exceptions hier fangen
        }


        /// <summary>
        /// Re-establishes connections to the MES4 system if they have been lost.
        /// </summary>
        /// <exception cref="ReconnectException">Thrown when a socket or I/O error occurs during reconnection.</exception>
        private void Reconnect()
        {
            try
            {
                DisconnectExistingConnections();
                EstablishNewConnections();
            }
            catch (SocketException ex)
            {
                throw new ReconnectException("Socket error during reconnection.", ex);
            }
            catch (IOException ex)
            {
                throw new ReconnectException("I/O error during reconnection.", ex);
            }
            // Weitere spezifische Exceptions hier fangen
        }

        /// <summary>
        /// Disconnects and cleans up existing TCP connections and network streams for both status and service communications.
        /// </summary>
        /// <remarks>
        /// Closes and nullifies the TCP clients and network streams for status and service communications.
        /// This method is typically called during the reconnection process to ensure that all existing connections are properly closed before establishing new ones.
        /// </remarks>
        private void DisconnectExistingConnections()
        {
            // Schließe die aktuelle Verbindung, wenn sie noch existiert
            if (statusTCPClient != null)
            {
                statusTCPClient.Close();
                statusTCPClient = null;
            }
            if (statusStream != null)
            {
                statusStream.Close();
                statusStream = null;
            }
            if (serviceTCPClient != null)
            {
                serviceTCPClient.Close();
                serviceTCPClient = null;
            }
            if (serviceStream != null)
            {
                serviceStream.Close();
                serviceStream = null;
            }
        }

        /// <summary>
        /// Establishes new TCP connections for both status and service communications with the MES4 system.
        /// </summary>
        private void EstablishNewConnections()
        {
            SafeDispose(statusStream);
            SafeDispose(statusTCPClient);
            SafeDispose(serviceStream);
            SafeDispose(serviceTCPClient);

            statusTCPClient = new TcpClient(MES4Host, MES4StatusPort);
            statusStream = statusTCPClient.GetStream();

            serviceTCPClient = new TcpClient(MES4Host, MES4ServicePort);
            serviceStream = serviceTCPClient.GetStream();
        }

        /// <summary>
        /// Creates a status message byte array based on the current status of the resource.
        /// </summary>
        /// <returns>A byte array representing the current status massage.</returns>
        private byte[] CreateStatusMessage()
        {
            byte[] message = new byte[4];

            byte highByte = (byte)(ResourceID >> 8);
            byte lowByte = (byte)(ResourceID & 0xFF);


            message[0] = highByte;
            message[1] = lowByte;

            if (ResourcePLCType == PLCType.LittleEndian)
                message[2] = 1;
            else
                message[2] = 2;

            message[3] = ResourceStatus.CreateStatusByteMessage();


            return message;
        }

        /// <summary>
        /// Interprets the XML header file to initialize header dictionaries for communication.
        /// </summary>
        /// <param name="filePath">The file path of the XML header.</param>
        /// <returns>An OrderedDictionary containing the interpreted header data.</returns>
        /// <exception cref="XmlException">Thrown when there is an error in parsing the XML file.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs while accessing the file.</exception>
        /// <exception cref="HeaderInterpretationException">Thrown when the header data cannot be correctly interpreted or if there is a format issue with the XML file.</exception>
        private static OrderedDictionary InterpretXMLHeader(string filePath)
        {
            OrderedDictionary headerList = new OrderedDictionary();

            try
            {
                XDocument xmlDoc = XDocument.Load(filePath);
                XNamespace ns = "http://tempuri.org/dsHeader.xsd";

                int IDpointer = 1;

                foreach (var dtHeader in xmlDoc.Descendants(ns + "dtHeader"))
                {
                    if (TryParseHeaderData(dtHeader, ns, out HeaderData? headerData))
                    {
                        if (headerData != null)
                        {
                            ValidateHeaderData(headerData, ref IDpointer, filePath);
                            headerList.Add(headerData.ParameterName, headerData);
                        }
                    }
                }
            }
            catch (XmlException ex)
            {
                throw new HeaderInterpretationException($"XML parsing error in {filePath}: {ex.Message}", ex);
            }
            catch (IOException ex)
            {
                throw new HeaderInterpretationException($"IO error while reading {filePath}: {ex.Message}", ex);
            }
            // Weitere spezifische Exceptions hier fangen

            return headerList;
        }

        /// <summary>
        /// Tries to parse header data from an XML element.
        /// </summary>
        /// <param name="dtHeader">The XML element representing the header data.</param>
        /// <param name="ns">The XML namespace used in the header file.</param>
        /// <param name="headerData">The parsed header data.</param>
        /// <returns>True if parsing is successful; otherwise, false.</returns>
        private static bool TryParseHeaderData(XElement dtHeader, XNamespace ns, out HeaderData? headerData)
        {
            headerData = null;

            if (int.TryParse(dtHeader.Element(ns + "ID")?.Value, out int id) &&
                int.TryParse(dtHeader.Element(ns + "Type")?.Value, out int type) &&
                int.TryParse(dtHeader.Element(ns + "StringLength")?.Value, out int stringLength) &&
                int.TryParse(dtHeader.Element(ns + "CoDeSysV3_Address")?.Value, out int address))
            {
                string parameterName = dtHeader.Element(ns + "ParameterName")?.Value ?? string.Empty;

                if (string.IsNullOrEmpty(parameterName))
                {
                    return false;
                }

                headerData = new HeaderData(id, parameterName, type, stringLength, address);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Validates the integrity and consistency of parsed header data.
        /// This method checks if the header data from the XML file is correctly formatted and contains all necessary information.
        /// It ensures that each header data item has a sequential ID and a valid parameter name.
        /// </summary>
        /// <param name="headerData">The header data to validate.</param>
        /// <param name="IDpointer">A reference to the expected ID counter for validation. This counter is incremented after successful validation.</param>
        /// <param name="filePath">The file path of the XML header, used for error messaging.</param>
        /// <exception cref="HeaderInterpretationException">
        /// Thrown when the ID of the header data does not match the expected sequence or when the parameter name is missing.
        /// The exception message includes details about the specific issue and the file path for easier troubleshooting.
        /// </exception>
        private static void ValidateHeaderData(HeaderData headerData, ref int IDpointer, string filePath)
        {
            if (headerData.ID != IDpointer)
                throw new HeaderInterpretationException($"The HeaderXML-File in {filePath} is incorrectly formatted. Missing ID {IDpointer}.");

            if (string.IsNullOrEmpty(headerData.ParameterName))
                throw new HeaderInterpretationException($"ParameterName is missing in the HeaderXML-File {filePath} for ID {IDpointer}.");

            IDpointer++;
        }




        #endregion


        #region Destruction

        /// <summary>
        /// Releases the unmanaged resources used by the MES4FestoConnector and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">True to release both managed and unmanaged resources; False to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    SafeDispose(statusStream);
                    SafeDispose(statusTCPClient);
                    SafeDispose(serviceStream);
                    SafeDispose(serviceTCPClient);
                    StopAndCleanUpStatusMessageThread();
                }

                disposed = true;
            }
        }


        /// <summary>
        /// Disposes the specified resource safely, handling any exceptions that might occur.
        /// </summary>
        /// <param name="resource">The resource to dispose.</param>
        private void SafeDispose(IDisposable? resource)
        {
            try
            {
                resource?.Dispose();
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Stops the status message thread and cleans up any resources associated with it.
        /// </summary>
        private void StopAndCleanUpStatusMessageThread()
        {
            if (StatusMessageThread != null)
            {
                if (StatusMessageThread.IsAlive)
                {
                    try
                    {
                        StatusMessageThread.Interrupt();
                        StatusMessageThread.Join();
                    }
                    catch (ThreadInterruptedException)
                    {
                        // Behandlung spezifischer Thread-Unterbrechungsprobleme
                        // keine
                    }
                    catch (Exception) { }
                }
                StatusMessageThread = null;
            }
        }


        // Optional: Finalizer nur überschreiben, wenn Dispose(bool disposing) Code für die Freigabe nicht verwalteter Ressourcen enthält
        //~MES4FestoConnector()
        //{
        //    // Ändern Sie diesen Code nicht. Fügen Sie Bereinigungscode in der Methode "Dispose(bool disposing)" ein.
        //    Dispose(disposing: false);
        //}


        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            // Ändern Sie diesen Code nicht. Fügen Sie Bereinigungscode in der Methode "Dispose(bool disposing)" ein.
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }


        #endregion

    }
}