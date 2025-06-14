using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Survival
{
    internal class Health : Belief<float>
    {
        public float Amount { get; private set; }
        public float MaxAmount { get; private set; }
    }
}
