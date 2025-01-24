namespace SCPSLBot.Navigation.Mesh
{
    internal struct LocalEdge
    {
        public LocalVertex From;
        public LocalVertex To;

        public LocalEdge(LocalVertex from, LocalVertex to)
        {
            From = from;
            To = to;
        }
        public override bool Equals(object obj)
        {
            return obj is LocalEdge edge && (From, To).Equals((edge.From, edge.To));
        }

        public override int GetHashCode()
        {
            return (From, To).GetHashCode();
        }

        public static bool operator ==(LocalEdge left, LocalEdge right)
        {
            return (left.From, left.To) == (right.From, right.To);
        }

        public static bool operator !=(LocalEdge left, LocalEdge right)
        {
            return !(left == right);
        }

        public override string ToString()
        {
            return (From, To).ToString();
        }
    }
}
