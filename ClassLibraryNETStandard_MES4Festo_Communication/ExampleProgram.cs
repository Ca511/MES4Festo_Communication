using ClassLibNETStand_MES4FestoConnector;
using System.Text;

namespace ConsoleApp_MES4Connector
{
    internal class ExampleProgram
    {
        private static readonly MES4FestoConnector.Status Status = new MES4FestoConnector.Status();

        private static readonly MES4FestoConnector Connector = new MES4FestoConnector("172.21.0.90", 50, MES4FestoConnector.PLCType.BigEndian, true, ref Status);


        static void Main(string[] args)
        {
            Connector.Connect();

            Connector.StatusMessageSent += Connector_StatusMessageSent;

            ServiceCaller.GetOpForRsc(Connector, 0, 50, out _, out Int16 StepNo, out Int16 OPos, out Int16 WPNo, out _, out _, out Int32 CNo, out Int32 PNo, out _);

            Console.WriteLine("Nächster Arbeitsschritt für die Resource 50: Arbeitsplan " + WPNo + "; Teil " + PNo + "; Schritt " + StepNo + "; Kunde " + CNo);

            Thread.Sleep(2000);
            Status.SetBusyBit(true);
            Thread.Sleep(2000); // Simulierte Abarbeitung
            Status.SetBusyBit(true);


            Connector.Disconnect();
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
