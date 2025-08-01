using SCPSLBot.Navigation.Mesh;
using System;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Navigation
{
    internal class NavigationCell(TransformCell transformCell, CellWithin cellWithin) : IBelief
    {
        public readonly TransformCell TransformCell = transformCell;
        public event Action OnUpdate;

        public bool IsWithin = false;

        public bool Is(TransformCell otherCell)
        {
            IsWithin = otherCell == TransformCell;

            return IsWithin;
        }

        public void Update()
        {
            var newIsWithin = cellWithin.TransformCell.HasValue && cellWithin.TransformCell.Value == TransformCell;
            if (newIsWithin != IsWithin)
            {
                IsWithin = newIsWithin;
                OnUpdate?.Invoke();
            }
        }

        public override string ToString()
        {
            return $"{nameof(NavigationCell)}({TransformCell}) {{ {nameof(IsWithin)} = {IsWithin} }}";
        }
    }
}
