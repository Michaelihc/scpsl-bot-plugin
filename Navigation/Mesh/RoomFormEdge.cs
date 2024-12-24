namespace SCPSLBot.Navigation.Mesh
{
    internal struct RoomFormEdge
    {
        public RoomFormVertex From;
        public RoomFormVertex To;

        public RoomFormEdge(RoomFormVertex from, RoomFormVertex to)
        {
            From = from;
            To = to;
        }
        public override bool Equals(object obj)
        {
            return obj is RoomFormEdge edge && (From, To).Equals((edge.From, edge.To));
        }

        public override int GetHashCode()
        {
            return (From, To).GetHashCode();
        }

        public static bool operator ==(RoomFormEdge left, RoomFormEdge right)
        {
            return (left.From, left.To) == (right.From, right.To);
        }

        public static bool operator !=(RoomFormEdge left, RoomFormEdge right)
        {
            return !(left == right);
        }

        public override string ToString()
        {
            return (From, To).ToString();
        }
    }
}
