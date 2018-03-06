﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HuskyRobotics.Utilities
{
    // This is just a tuple that can have its fields modified. 
    public struct MutableTuple<T1, T2>
    {
        public T1 Item1;
        public T2 Item2;

        public MutableTuple(T1 item1, T2 item2) { Item1 = item1; Item2 = item2; }
    }
}
