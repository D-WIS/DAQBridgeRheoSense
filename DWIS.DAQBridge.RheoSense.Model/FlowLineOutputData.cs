namespace DWIS.DAQBridge.RheoSense.Model;

// Legacy return-flow identity, explicitly between annulus outlet and shale shakers.
public class FlowLineOutputData() : RheometerOutputData(new() { Id = "FlowLine", Placement = PlacementProfile.ReturnBeforeShakers });
