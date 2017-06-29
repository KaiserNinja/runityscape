﻿using System;
using System.ComponentModel;

namespace Scripts.Game.Serialized {
    public enum TimeOfDay {
        [Description("<color=white>Dawn</color>")]
        DAWN,
        [Description("<color=yellow>Morning</color>")]
        MORNING,
        [Description("<color=orange>Midday</color>")]
        MIDDAY,
        [Description("<color=red>Afternoon</color>")]
        AFTERNOON,
        [Description("<color=magenta>Dusk</color>")]
        DUSK,
        [Description("<color=blue>Night</color>")]
        NIGHT,
    }

    [Serializable]
    public class Flags {
        public TimeOfDay Time;
        public int DayCount;

        public Flags() { }
    }
}