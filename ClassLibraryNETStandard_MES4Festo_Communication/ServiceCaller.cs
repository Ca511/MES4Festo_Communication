using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp1
{
    public static class ServiceCaller
    {
        /*  GetOpForRsc
            SELECT tblOrderPos.StepNo, tblOrderPos.ONo, tblOrderPos.OPos, tblOrderPos.WPNo, tblOrderPos.ResourceID, tblOrderPos.OpNo, tblOrderPos.MainOPos, tblOrder.CNo, tblOrderPos.PNo, tblStep.ErrorStepNo
            FROM (tblOrder INNER JOIN tblOrderPos ON tblOrder.ONo = tblOrderPos.ONo) INNER JOIN tblStep ON (tblOrderPos.StepNo = tblStep.StepNo) AND (tblOrderPos.StepNo = tblStep.StepNo) AND (tblOrderPos.OPos = tblStep.OPos) AND (tblOrderPos.ONo = tblStep.ONo)
            WHERE (((tblOrderPos.ResourceID)=#ResourceID) AND ((tblOrderPos.PlannedStart)<=Now()) AND ((tblOrderPos.State)<10) AND ((tblOrder.Enabled)=True) AND ((tblOrderPos.subOrderBlocked)=False))
            ORDER BY tblOrderPos.PlannedStart, tblOrderPos.ONo, tblOrderPos.OPos;
        */
        public static void GetOpForRsc(MES4FestoConnector connector, Int16 ErrorState_in, Int16 ResourceID_in, out Int16 ErrorState, out Int16 StepNo, out Int32 ONo, out Int16 OPos, out Int16 WPNo, out Int16 ResourceID, out Int16 OpNo, out Int32 CNo, out Int32 PNo)
        {
            var StandardInputParameters = new Dictionary<string, object>();
            var ServiceSpecificParameters = new Dictionary<string, object>();

            StandardInputParameters.Add("#ResourceID", ResourceID_in);

            var RequestServicePackage = new MES4FestoConnector.ServicePackage(connector, 100, 1, ErrorState_in, StandardInputParameters, ServiceSpecificParameters);

            connector.CallService(RequestServicePackage, out MES4FestoConnector.ServicePackage ResponseServicePackage);

            ErrorState = (Int16)ResponseServicePackage.ErrorState;


            StepNo = (Int16)ResponseServicePackage.StandardParameters["StepNo"];
            ONo = (Int32)ResponseServicePackage.StandardParameters["ONo"];
            OPos = (Int16)ResponseServicePackage.StandardParameters["OPos"];
            WPNo = (Int16)ResponseServicePackage.StandardParameters["WPNo"];
            ResourceID = (Int16)ResponseServicePackage.StandardParameters["ResourceID"];
            OpNo = (Int16)ResponseServicePackage.StandardParameters["OpNo"];
            // MainOPos = (Int16)ResponseServicePackage.StandardParameters["MainOPos"];
            CNo = (Int32)ResponseServicePackage.StandardParameters["CNo"];
            PNo = (Int32)ResponseServicePackage.StandardParameters["PNo"];
            // ErrorStepNo = (Int16)ResponseServicePackage.StandardParameters["ErrorStepNo"];
        }

        public static void GetCustomerInfo(MES4FestoConnector connector, Int16 ErrorState_in, Int32 ONo_in, out Int16 ErrorState, out String FirstName, out String LastName, out String Company)
        {
            var StandardInputParameters = new Dictionary<string, object>();
            var ServiceSpecificParameters = new Dictionary<string, object>();

            StandardInputParameters.Add("#ONo", ONo_in);

            var RequestServicePackage = new MES4FestoConnector.ServicePackage(connector, 100, 31, ErrorState_in, StandardInputParameters, ServiceSpecificParameters);

            connector.CallService(RequestServicePackage, out MES4FestoConnector.ServicePackage ResponseServicePackage);

            ErrorState = (Int16)ResponseServicePackage.ErrorState;

            FirstName = (String)ResponseServicePackage.ServiceSpecificParameters["FirstName"];
            LastName = (String)ResponseServicePackage.ServiceSpecificParameters["LastName"];
            Company = (String)ResponseServicePackage.ServiceSpecificParameters["Company"];
        }

    }
}
