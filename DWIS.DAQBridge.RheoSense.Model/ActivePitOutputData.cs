namespace DWIS.DAQBridge.RheoSense.Model;

// Legacy identity adapter; signal attributes live in the instrument model.
public class ActivePitOutputData() : RheometerOutputData(new() { Id = "ActivePit", Placement = PlacementProfile.ActivePit });
