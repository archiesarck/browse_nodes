using System;
using System.Collections.Generic;

namespace browse_nodes
{
    /// <summary>
    /// Serializable data structure for persisting graph state
    /// </summary>
    [Serializable]
    public class NodeData
    {
        public string Text { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    [Serializable]
    public class LinkData
    {
        public int FromNodeIndex { get; set; }
        public int ToNodeIndex { get; set; }
    }

    [Serializable]
    public class GraphData
    {
        public List<NodeData> Nodes { get; set; } = new List<NodeData>();
        public List<LinkData> Links { get; set; } = new List<LinkData>();
    }
}

