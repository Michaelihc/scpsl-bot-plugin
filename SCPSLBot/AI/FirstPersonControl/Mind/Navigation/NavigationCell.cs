using SCPSLBot.Navigation.Mesh;
using System;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Navigation
{
    internal class NavigationCell(TransformCell transformCell, CellWithin cellWithin) : IBelief
    {
        public readonly TransformCell TransformCell = transformCell;
        public event Action OnUpdate;

        public bool IsWithin = false;

        public void Update()
        {
            var newIsWithin = cellWithin.TransformCell == TransformCell;
            if (newIsWithin != IsWithin)
            {
                IsWithin = newIsWithin;
                OnUpdate?.Invoke();
            }
        }
    }
}
