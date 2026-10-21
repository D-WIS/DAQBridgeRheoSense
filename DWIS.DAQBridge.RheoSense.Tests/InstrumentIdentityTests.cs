using DWIS.DAQBridge.RheoSense.Model;
using DWIS.RigOS.Common.Worker;

namespace DWIS.DAQBridge.RheoSense.Tests;

public class InstrumentIdentityTests
{
    [Fact]
    public void BothPositionsPublishInstrumentModelFields()
    {
        foreach (DWISData data in new DWISData[] { new ActivePitOutputData(), new FlowLineOutputData() })
        {
            Assert.Contains(data.Manifests.Value.Values, m => m.InjectedNodes.Any(n => n.UniqueName == "rheoSenseModel"));
            foreach (var manifest in data.Manifests.Value.Values)
            {
                var models = manifest.InjectedNodes.Where(n => n.UniqueName == "rheoSenseModel").ToArray();
                // Chosen extrapolation parameters have no measurement-device association.
                if (models.Length == 0) continue;
                Assert.Contains(models.Single().Fields!, f => f.FieldName == "ModelName" && f.FieldValue == "RheoSense");
                Assert.Contains(models.Single().Fields!, f => f.FieldName == "ModelIdentifier" && f.FieldValue == "IMS/RheoSense");
                Assert.Contains(manifest.InjectedReferences, r => r.VerbURI == "http://ddhub.no/IsOfInstrumentModel" && r.Object.ID == "rheoSenseModel");
                Assert.Contains(manifest.InjectedReferences, r => r.VerbURI == "http://ddhub.no/HasManufacturer" && r.Object.ID == "ims");
            }
        }
    }
}
