using ClassLibNETStand_MES4FestoConnector;
using System.Text;

namespace ConsoleApp_MES4Connector
{
    internal class Program
    {
        // Erstellen einer Status-Variablen, die den Zustand der Resource beschreibt und deren Inhalt sekündlich an das MES gemeldet wird.
        private static readonly MES4FestoConnector.Status Status = new MES4FestoConnector.Status(MES4FestoConnector.Status.Mode.AutoMode);

        // Erstellen einer Connector-Variablen, mit die Kommunikation mit dem MES gesteuert wird.
        private static readonly MES4FestoConnector Connector = new MES4FestoConnector("172.21.0.90", 50, MES4FestoConnector.PLCType.BigEndian, true, ref Status);


        static void Main(string[] args)
        {

            // Verbinden zum MES
            Connector.Connect();











            Connector.Dispose();




















            //Connector.ServiceRequestSent += Connector_ServiceRequestSent;
            Connector.StatusMessageSent += Connector_StatusMessageSent;


            // Erstellen einer Dictionary für alle Standard Service-Parameter und hinzufügen aller Werte (OrderNummer

            var StandardInputParameters = new Dictionary<string, object>();

            string ONo_paramName = "#ONo";
            Int32 ONo_input = 3352;

            string OPos_paramName = "#OPos";
            Int32 OPos_input = 1;

            StandardInputParameters.Add(ONo_paramName, ONo_input);
            StandardInputParameters.Add(OPos_paramName, OPos_input);


            // Erstellen einer Dictionary für alle Service-spezifischen Parameter und hinzufügen aller Werte (hier keine notwendig)

            var ServiceSpecificParameters = new Dictionary<string, object>();


            // Erstellen eines Request-ServicePackages, das eine Datenabfrage im MES steuert
            // Hier: Service 100, 33 GetStepDescription

            var RequestServicePackage = new MES4FestoConnector.ServicePackage(Connector, 100, 33, 0, StandardInputParameters, ServiceSpecificParameters);


            // Aufrufen des Service und entgegenehmen der Antwort

            //var ResponsePackage = Connector.CallService(RequestServicePackage);



            // Konsolenausgabe: Inspektion<110 mal 0x00>
            // Beschreibung, was mit dem Auftrag 1200 an der Position 1 von der Anfragenden Resource zu tun ist


            // Änderung des Status bit für den Zustand "busy"
            //Status.SetBusyBit(true);

            // Simulierte Abarbeitung
            //Thread.Sleep(10000);

            // Beenden der Verbindung
            //Connector.Disconnect();




            while (true)
            {
                Console.ReadKey();
                Status.SetBusyBit(true);
                Console.ReadKey();
                Status.SetBusyBit(false);
            }
        }

        private static void Connector_StatusMessageSent(object? sender, MES4FestoConnector.StatusMessageSentEventArgs e)
        {
            Console.WriteLine("Status Message sent:  " + ByteArrayToBinaryString(e.Message));


            string ByteArrayToBinaryString(byte[] byteArray)
            {
                StringBuilder binaryString = new StringBuilder();

                foreach (byte b in byteArray)
                {
                    binaryString.Append(Convert.ToString(b, 2).PadLeft(8, '0'));
                    binaryString.Append(" "); // Optional: Fügt ein Leerzeichen zwischen den Bytes für bessere Lesbarkeit hinzu
                }

                return binaryString.ToString().Trim();
            }
        }
    }









}
