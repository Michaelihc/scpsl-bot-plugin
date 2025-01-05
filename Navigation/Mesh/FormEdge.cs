namespace SCPSLBot.Navigation.Mesh
{
    internal struct FormEdge
    {
        public FormVertex From;
        public FormVertex To;

        public FormEdge(FormVertex from, FormVertex to)
        {
            From = from;
            To = to;
        }
        public override bool Equals(object obj)
        {
            return obj is FormEdge edge && (From, To).Equals((edge.From, edge.To));
        }

        public override int GetHashCode()
        {
            return (From, To).GetHashCode();
        }

        public static bool operator ==(FormEdge left, FormEdge right)
        {
            return (left.From, left.To) == (right.From, right.To);
        }

        public static bool operator !=(FormEdge left, FormEdge right)
        {
            return !(left == right);
        }

        public override string ToString()
        {
            return (From, To).ToString();
        }
    }
}
