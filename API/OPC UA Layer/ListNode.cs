using API.OPCUALayer;

namespace API.OPC_UA_Layer
{
    public class ListNode
    {
        public string id;
        public string nodeClass;
        public string accessLevel;
        public string executable;
        public string eventNotifier;
        public bool children;
        public Tree childrenNode;

        public string ImageUrl { get; set; }

        public string NodeName { get; set; }

        public ListNode()
        {
        }
    }
}
