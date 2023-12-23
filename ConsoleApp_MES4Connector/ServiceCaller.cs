using ClassLibNETStand_MES4FestoConnector;

namespace ConsoleApp_MES4Connector
{
    public static class ServiceCaller
    {
        /*  GetOpForRsc
            SELECT tblOrderPos.StepNo, tblOrderPos.ONo, tblOrderPos.OPos, tblOrderPos.WPNo, tblOrderPos.ResourceID, tblOrderPos.OpNo, tblOrderPos.MainOPos, tblOrder.CNo, tblOrderPos.PNo, tblStep.ErrorStepNo
            FROM (tblOrder INNER JOIN tblOrderPos ON tblOrder.ONo = tblOrderPos.ONo) INNER JOIN tblStep ON (tblOrderPos.StepNo = tblStep.StepNo) AND (tblOrderPos.StepNo = tblStep.StepNo) AND (tblOrderPos.OPos = tblStep.OPos) AND (tblOrderPos.ONo = tblStep.ONo)
            WHERE (((tblOrderPos.ResourceID)=#ResourceID) AND ((tblOrderPos.PlannedStart)<=Now()) AND ((tblOrderPos.State)<10) AND ((tblOrder.Enabled)=True) AND ((tblOrderPos.subOrderBlocked)=False))
            ORDER BY tblOrderPos.PlannedStart, tblOrderPos.ONo, tblOrderPos.OPos;
        */
        public static void GetOpForRsc(MES4FestoConnector connector, Int16 ErrorState, Int16 ResourceID_in, out Int16 StepNo, out Int16 OPos, out Int16 WPNo, out Int16 ResourceID, out Int16 MainOPos, out Int32 CNo, out Int32 PNo, out Int16 ErrorStepNo)
        {
            var StandardInputParameters = new Dictionary<string, object>();
            var ServiceSpecificParameters = new Dictionary<string, object>();

            StandardInputParameters.Add("#ResourceID", ResourceID_in);

            var RequestServicePackage = new MES4FestoConnector.ServicePackage(connector, 100, 1, ErrorState, StandardInputParameters, ServiceSpecificParameters);

            var ResponsePackage = connector.CallService(RequestServicePackage);

            StepNo = (Int16)ResponsePackage.StandardParameters["ResourceID"];
            OPos = (Int16)ResponsePackage.StandardParameters["OPos"];
            WPNo = (Int16)ResponsePackage.StandardParameters["WPNo"];
            ResourceID = (Int16)ResponsePackage.StandardParameters["ResourceID"];
            MainOPos = (Int16)ResponsePackage.StandardParameters["MainOPos"];
            CNo = (Int32)ResponsePackage.StandardParameters["CNo"];
            PNo = (Int32)ResponsePackage.StandardParameters["PNo"];
            ErrorStepNo = (Int16)ResponsePackage.StandardParameters["ErrorStepNo"];
        }

    }
}
