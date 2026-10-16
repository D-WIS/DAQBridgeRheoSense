using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DWIS.DAQBridge.RheoSense.Model
{
    public class Configuration
    {
        public TimeSpan LoopDuration { get; set; } = TimeSpan.FromSeconds(1.0);
        public string? GeneralBlackboard { get; set; } = "opc.tcp://localhost:48030";

    }
}
