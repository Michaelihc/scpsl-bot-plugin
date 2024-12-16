namespace SCPSLBot.Navigation.Mesh.Connector
{
    internal struct ConnectorFormEdge
    {
        public ConnectorFormVertex From;
        public ConnectorFormVertex To;

        public ConnectorFormEdge(ConnectorFormVertex from, ConnectorFormVertex to)
        {
            From = from;
            To = to;
        }
        public override bool Equals(object obj)
        {
            return obj is ConnectorFormEdge edge && (From, To).Equals((edge.From, edge.To));
        }

        public override int GetHashCode()
        {
            return (From, To).GetHashCode();
        }

        public static bool operator ==(ConnectorFormEdge left, ConnectorFormEdge right)
        {
            return (left.From, left.To) == (right.From, right.To);
        }

        public static bool operator !=(ConnectorFormEdge left, ConnectorFormEdge right)
        {
            return !(left == right);
        }

        public override string ToString()
        {
            return (From, To).ToString();
        }
    }
}
