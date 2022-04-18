using API.Monitoring;
using Opc.Ua.Client;

namespace API.Models.OPCUA
{
    public class MonitorPublishInfo
    {
        public string Topic { get; set; }
        public string BrokerUrl { get; set; }
        public Subscription Subscription { get; set; }
        public IPublisher Publisher { get; set; }

        public void Forward(string message)
        {
            Publisher.Publish(Topic, message);
        }
    }
}