using System.Text.RegularExpressions;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Microsoft.AspNetCore.SignalR;
using API.Models.OPCUA;
using API.HubConfig;
using API.Monitoring;
using API.Models.DataSet;
using API.OPC_UA_Layer;
using API.Exceptions;
using StatusCodes = Opc.Ua.StatusCodes;
using API.Extensions;

namespace API.OPCUALayer
{
    public interface IUaClient
    {
        public EndpointDescriptionCollection GetEndpoint(string serverUrl);
        Task<Node> ReadNodeAsync(string serverUrl, string nodeIdStr);
        Task<Node> ReadNodeAsync(string serverUrl, NodeId nodeId);
        Task<IEnumerable<EdgeDescription>> BrowseAsync(string serverUrl, string nodeToBrowseIdStr);
        Task<Tree> GetRootNode(string serverUrl, bool useSecurity);
        Task<Tree> GetChildren(string serverUrl,string node);
        public Tree ChildBrowse(ReferenceDescriptionCollection references, Session session, string node);
        Task<UaValue> ReadUaValueAsync(string serverUrl, VariableNode varNode);
        Task<string> GetDeadBandAsync(string serverUrl, VariableNode varNode);
        Task<bool> WriteNodeValueAsync(string serverUrl, VariableNode variableNode, VariableState state);
        Task<bool> IsFolderTypeAsync(string serverUrlstring, string nodeIdStr);
        Task<bool> IsServerAvailable(string serverUrlstring);
        Task<bool> ServerDisconenctAsync(string serverUrlstring);
        Task<bool[]> CreateMonitoredItemsAsync(string serverUrl, MonitorableNode[] monitorableNodes, string brokerUrl, string topic);
        Task<bool> DeleteMonitoringPublish(string serverUrl, string brokerUrl, string topic);
        public void SetUseSecurity(bool _useSecurity);
    }

    public interface IUaClientSingleton : IUaClient {
    
    }

    public class UaClient : IUaClientSingleton
    {
        private readonly ApplicationInstance _application;
        private ApplicationConfiguration _appConfiguration;
        private bool _autoAccept;
        public bool useSecurity = true ;


        //A Dictionary containing al the active Sessions, indexed per server Id.
        private readonly Dictionary<string, Session> _sessions;
        private readonly Dictionary<string, List<MonitorPublishInfo>> _monitorPublishInfo;
        private IHubContext<MyHub> _hub;
        public UaClient(IHubContext<MyHub> hub)
        {
            _application = new ApplicationInstance
            {
                ApplicationType = ApplicationType.Client,
                ConfigSectionName = "OPCUAWebPlatform"
            };
            _hub = hub;
            _sessions = new Dictionary<string, Session>();
            _monitorPublishInfo = new Dictionary<string, List<MonitorPublishInfo>>();
        }
        public void SetUseSecurity(bool _useSecurity) {
            useSecurity = _useSecurity;
        }
        public async Task<bool> ServerDisconenctAsync(string serverUrlstring)
        {
            try
            {
                Session session = await GetSessionAsync(serverUrlstring);
                session.Close(10000);
                return true;
            }
            catch
            {
                return false;
            }
        }
        public EndpointDescriptionCollection GetEndpoint(string serverUrl)
        {
           
            try
            {
                Uri uri = new Uri(serverUrl);

                DiscoveryClient client = DiscoveryClient.Create(uri);

                EndpointDescriptionCollection endpoints = client.GetEndpoints(null);
                client.Close();
                client.Dispose();
                return endpoints;
            }
            catch (Exception e)
            {
                return null;
            }
        }
        public async Task<Node> ReadNodeAsync(string serverUrl, string nodeIdStr)
        {
            Session session = await GetSessionAsync(serverUrl);
            NodeId nodeToRead = PlatformUtils.ParsePlatformNodeIdString(nodeIdStr);
            var node = session.ReadNode(nodeToRead);
            return node;
        }

        public async Task<Node> ReadNodeAsync(string serverUrl, NodeId nodeToRead)
        {
            Session session = await GetSessionAsync(serverUrl);
            Node node;
            node = session.ReadNode(nodeToRead);
            return node;
        }


        public async Task<bool> WriteNodeValueAsync(string serverUrl, VariableNode variableNode, VariableState state)
        {
            Session session = await GetSessionAsync(serverUrl);
            var typeManager = new DataTypeManager(session);
            WriteValueCollection writeValues = new WriteValueCollection();

            WriteValue writeValue = new WriteValue
            {
                NodeId = variableNode.NodeId,
                AttributeId = Attributes.Value,
                Value = typeManager.GetDataValueFromVariableState(state, variableNode)
            };

            writeValues.Add(writeValue);

            session.Write(null, writeValues, out var results, out _);
            if (!StatusCode.IsGood(results[0]))
            {
                if (results[0] == StatusCodes.BadTypeMismatch)
                    throw new ValueToWriteTypeException("Wrong Type Error: data sent are not of the type expected. Check your data and try again");
                throw new ValueToWriteTypeException(results[0].ToString());
            }
            return true;
        }

        public async Task<Tree> GetRootNode(string serverUrl,bool _useSecurity)
        {
            Console.WriteLine(useSecurity);
            Session session = await GetSessionAsync(serverUrl);
            ReferenceDescriptionCollection references;
            Byte[] continuationPoint ;
            Tree browserTree = new Tree();

            try
            {
                session.Browse(
                    null,
                    null,
                    ObjectIds.ObjectsFolder,
                    0u,
                    BrowseDirection.Forward,
                    ReferenceTypeIds.HierarchicalReferences,
                    true,
                    0,
                    out continuationPoint,
                    out references);
                Console.WriteLine(continuationPoint);

                browserTree.currentView.Add(new ListNode { id = ObjectIds.ObjectsFolder.ToString(), NodeName = "Root", children = (references?.Count != 0) });

                return browserTree;
            }
            catch
            {
                session.Close();
                return null;
            }
        }

        public async Task<Tree> GetChildren(string serverUrl,string node)
        {
            Session session = await GetSessionAsync(serverUrl);
            ReferenceDescriptionCollection references ;
            ReferenceDescriptionCollection childReferences = null;
            Byte[] childContinuationPoint;
            Byte[] continuationPoint;
            BrowseResultCollection results;
            Tree browserTree = new Tree();

            try
            {
                session.Browse(
                    null, null, node, 0u, BrowseDirection.Forward,
                    ReferenceTypeIds.HierarchicalReferences,
                    true,
                    0,
                    out continuationPoint,
                    out references);

                while (continuationPoint != null)
                {
                    session.BrowseNext(
                        null,
                        false,
                        continuationPoint,
                        out childContinuationPoint,
                        out childReferences);
                    references.AddRange(childReferences);
                    continuationPoint = childContinuationPoint;
                }

                return browserTree = ChildBrowse(references, session,node);
            }
            catch
            {
                session.Close();
                return null;
            }
        }

        public Tree ChildBrowse(ReferenceDescriptionCollection references, Session session, string node)
        {
            Tree browserTree = new Tree();
            if (references != null)
            {
                foreach (var nodeReference in references)
                {
                    INode currentNode = null;
                    try
                    {
                        currentNode = session.ReadNode(ExpandedNodeId.ToNodeId(nodeReference.NodeId, session.NamespaceUris));
                    }
                    catch (Exception)
                    {
                        // skip this node
                        continue;
                    }

                    byte currentNodeAccessLevel = 0;
                    byte currentNodeEventNotifier = 0;
                    bool currentNodeExecutable = false;

                    VariableNode variableNode = currentNode as VariableNode;
                    if (variableNode != null)
                    {
                        currentNodeAccessLevel = variableNode.UserAccessLevel;
                        currentNodeAccessLevel = (byte)((uint)currentNodeAccessLevel & ~0x2);
                    }

                    ObjectNode objectNode = currentNode as ObjectNode;
                    if (objectNode != null)
                    {
                        currentNodeEventNotifier = objectNode.EventNotifier;
                    }

                    ViewNode viewNode = currentNode as ViewNode;
                    if (viewNode != null)
                    {
                        currentNodeEventNotifier = viewNode.EventNotifier;
                    }

                    MethodNode methodNode = currentNode as MethodNode;
                    if (methodNode != null)
                    {
                        currentNodeExecutable = methodNode.UserExecutable;
                    }

                    browserTree.currentView.Add(new ListNode()
                    {
                        id = nodeReference.NodeId.ToString(),
                        NodeName = nodeReference.DisplayName.Text.ToString(),
                        nodeClass = nodeReference.NodeClass.ToString(),
                        accessLevel = currentNodeAccessLevel.ToString(),
                        eventNotifier = currentNodeEventNotifier.ToString(),
                        executable = currentNodeExecutable.ToString(),
                        children = (references?.Count != 0),
                        ImageUrl = (nodeReference.NodeClass.ToString() == "Variable") ? "folderOpen.jpg" : "folder.jpg"
                    });
                    if (browserTree.currentView[0].ImageUrl == null)
                    {
                        browserTree.currentView[0].ImageUrl = "";
                    }
                }
                if (browserTree.currentView.Count == 0)
                {
                    INode currentNode = session.ReadNode(new NodeId(node));

                    byte currentNodeAccessLevel = 0;
                    byte currentNodeEventNotifier = 0;
                    bool currentNodeExecutable = false;

                    VariableNode variableNode = currentNode as VariableNode;

                    if (variableNode != null)
                    {
                        currentNodeAccessLevel = variableNode.UserAccessLevel;
                        currentNodeAccessLevel = (byte)((uint)currentNodeAccessLevel & ~0x2);
                    }

                    ObjectNode objectNode = currentNode as ObjectNode;

                    if (objectNode != null)
                    {
                        currentNodeEventNotifier = objectNode.EventNotifier;
                    }

                    ViewNode viewNode = currentNode as ViewNode;

                    if (viewNode != null)
                    {
                        currentNodeEventNotifier = viewNode.EventNotifier;
                    }

                    MethodNode methodNode = currentNode as MethodNode;

                    if (methodNode != null)
                    {
                        currentNodeExecutable = methodNode.UserExecutable;
                    }

                    browserTree.currentView.Add(new ListNode()
                    {
                        id = node,
                        NodeName = currentNode.DisplayName.Text.ToString(),
                        nodeClass = currentNode.NodeClass.ToString(),
                        accessLevel = currentNodeAccessLevel.ToString(),
                        eventNotifier = currentNodeEventNotifier.ToString(),
                        executable = currentNodeExecutable.ToString(),
                        children = false,
                        ImageUrl = null
                    });
                }
                
            }

            
            return browserTree;
        }

        public async Task<IEnumerable<EdgeDescription>> BrowseAsync(string serverUrl, string nodeToBrowseIdStr)
        {
            Session session = await GetSessionAsync(serverUrl);
            NodeId nodeToBrowseId = PlatformUtils.ParsePlatformNodeIdString(nodeToBrowseIdStr);

            var browser = new Browser(session)
            {
                NodeClassMask = (int)NodeClass.Method | (int)NodeClass.Object | (int)NodeClass.Variable,
                ResultMask = (uint)BrowseResultMask.DisplayName | (uint)BrowseResultMask.NodeClass | (uint)BrowseResultMask.ReferenceTypeInfo,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences
            };

            return browser.Browse(nodeToBrowseId)
                .Select(rd => new EdgeDescription(rd.NodeId.ToStringId(session.MessageContext.NamespaceUris),
                    rd.DisplayName.Text,
                    rd.NodeClass,
                    rd.ReferenceTypeId));
            

        }

        public async Task<bool> IsFolderTypeAsync(string serverUrl, string nodeIdStr)
        {
            Session session = await GetSessionAsync(serverUrl);
            NodeId nodeToBrowseId = PlatformUtils.ParsePlatformNodeIdString(nodeIdStr);

            //Set a Browser object to follow HasTypeDefinition Reference only
            var browser = new Browser(session)
            {
                ResultMask = (uint)BrowseResultMask.DisplayName | (uint)BrowseResultMask.TargetInfo,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasTypeDefinition
            };


            ReferenceDescription refDescription = browser.Browse(nodeToBrowseId)[0];
            NodeId targetId = ExpandedNodeId.ToNodeId(refDescription.NodeId, session.MessageContext.NamespaceUris);

            //Once got the Object Type, set the browser to follow Type hierarchy in inverse order.
            browser.ReferenceTypeId = ReferenceTypeIds.HasSubtype;
            browser.BrowseDirection = BrowseDirection.Inverse;

            while (targetId != ObjectTypeIds.FolderType && targetId != ObjectTypeIds.BaseObjectType)
            {
                refDescription = browser.Browse(targetId)[0];
                targetId = ExpandedNodeId.ToNodeId(refDescription.NodeId, session.MessageContext.NamespaceUris);
            }
            return targetId == ObjectTypeIds.FolderType;
        }

        public async Task<UaValue> ReadUaValueAsync(string serverUrl, VariableNode variableNode)
        {
            Session session = await GetSessionAsync(serverUrl);
            var typeManager = new DataTypeManager(session);

            return typeManager.GetUaValue(variableNode);
        }

        public async Task<bool> IsServerAvailable(string serverUrlstring)
        {
            Session session;
            try
            {
                session = await GetSessionAsync(serverUrlstring);
            }
            catch (Exception exc)
            {
                return false;
            }
            if (session.IsServerStatusGood())
                return true;
            return await RestoreSessionAsync(serverUrlstring);
        }


        public async Task<string> GetDeadBandAsync(string serverUrl, VariableNode varNode)
        {
            Session session = await GetSessionAsync(serverUrl);
            var dataTypeId = varNode.DataType;

            var browse = new Browser(session)
            {
                ResultMask = (uint)BrowseResultMask.TargetInfo,
                BrowseDirection = BrowseDirection.Inverse,
                ReferenceTypeId = ReferenceTypeIds.HasSubtype
            };

            while (!(dataTypeId.Equals(DataTypeIds.Number)) && !(dataTypeId.Equals(DataTypeIds.BaseDataType)))
            {
                dataTypeId = ExpandedNodeId.ToNodeId(browse.Browse(dataTypeId)[0].NodeId, session.MessageContext.NamespaceUris);
            }

            var isAbsolute = (dataTypeId == DataTypeIds.Number);

            browse.BrowseDirection = BrowseDirection.Forward;
            browse.ReferenceTypeId = ReferenceTypeIds.HasProperty;
            var rdc = browse.Browse(varNode.NodeId);

            var isPercent = rdc.Exists(rd => rd.BrowseName.Name.Equals("EURange"));

            if (isAbsolute)
            {
                return isPercent ? "Absolute, Percentage" : "Absolute";
            }

            return isPercent ? "Percentage" : "None";

        }

        public async Task<bool[]> CreateMonitoredItemsAsync(string serverUrl, MonitorableNode[] monitorableNodes,
            string brokerUrl, string topic)
        {
            var session = await GetSessionAsync(serverUrl);

            MonitorPublishInfo monitorInfo;

            const string pattern = @"^(mqtt|signalr):(.*)$";
            var match = Regex.Match(brokerUrl, pattern);
            var protocol = match.Groups[1].Value;
            var url = match.Groups[2].Value;
            var publisher = PublisherFactory.GetPublisherForProtocol(protocol, url);

            //Set publishInterval to minimum samplinginterval
            var publishInterval = monitorableNodes.Select(elem => elem.SamplingInterval).Min();

            lock (_monitorPublishInfo)
            {
                //Check if a Subscription for the
                if (_monitorPublishInfo.ContainsKey(serverUrl))
                {
                    monitorInfo = _monitorPublishInfo[serverUrl].FirstOrDefault(info => info.Topic == topic && info.BrokerUrl == url);
                    if (monitorInfo == null)
                    {
                        monitorInfo = new MonitorPublishInfo()
                        {
                            Topic = topic,
                            BrokerUrl = url,
                            Subscription = CreateSubscription(session, publishInterval, 0),
                            Publisher = publisher
                        };
                        _monitorPublishInfo[serverUrl].Add(monitorInfo);
                    }
                    else if (monitorInfo.Subscription.PublishingInterval > publishInterval)
                    {
                        monitorInfo.Subscription.PublishingInterval = publishInterval;
                        monitorInfo.Subscription.Modify();
                    }
                }
                else
                {
                    monitorInfo = new MonitorPublishInfo()
                    {
                        Topic = topic,
                        BrokerUrl = url,
                        Subscription = CreateSubscription(session, publishInterval, 0),
                        Publisher = publisher
                    };
                    var list = new List<MonitorPublishInfo> { monitorInfo };
                    _monitorPublishInfo.Add(serverUrl, list);
                }
            }

            var createdMonitoredItems = new List<MonitoredItem>();

            foreach (var monitorableNode in monitorableNodes)
            {
                var mi = new MonitoredItem()
                {
                    StartNodeId = PlatformUtils.ParsePlatformNodeIdString(monitorableNode.NodeId),
                    DisplayName = monitorableNode.NodeId,
                    SamplingInterval = monitorableNode.SamplingInterval
                };

                if (monitorableNode.DeadBand != "none")
                {
                    mi.Filter = new DataChangeFilter()
                    {
                        Trigger = DataChangeTrigger.StatusValue,
                        DeadbandType = (uint)(DeadbandType)Enum.Parse(typeof(DeadbandType), monitorableNode.DeadBand, true),
                        DeadbandValue = monitorableNode.DeadBandValue
                    };
                }

                mi.Notification += OnMonitorNotification;
                monitorInfo.Subscription.AddItem(mi);
                var monitoredItems = monitorInfo.Subscription.CreateItems();
                createdMonitoredItems.AddRange(monitoredItems);
            }

            var results = createdMonitoredItems.Distinct().Select(m => m.Created).ToArray();
            foreach (var monitoredItem in createdMonitoredItems.Where(m => !m.Created))
            {
                monitorInfo.Subscription.RemoveItem(monitoredItem);
            }

            return results;
        }




        public async Task<bool> DeleteMonitoringPublish(string serverUrl, string brokerUrl, string topic)
        {
            var session = await GetSessionAsync(serverUrl);

            lock (_monitorPublishInfo)
            {
                if (!_monitorPublishInfo.ContainsKey(serverUrl)) return false;

                const string pattern = @"^(mqtt|signalr):(.*)$";
                var match = Regex.Match(brokerUrl, pattern);
                brokerUrl = match.Groups[2].Value;

                var monitorPublishInfo = _monitorPublishInfo[serverUrl].Find(mpi => mpi.Topic == topic && mpi.BrokerUrl == brokerUrl);

                if (monitorPublishInfo == null) return false;

                try
                {
                    session.DeleteSubscriptions(null, new UInt32Collection(new[] { monitorPublishInfo.Subscription.Id }), out var _, out var _);
                }
                catch (ServiceResultException e)
                {
                    Console.WriteLine(e);
                    return false;
                }

                _monitorPublishInfo[serverUrl].Remove(monitorPublishInfo);
                if (_monitorPublishInfo[serverUrl].Count == 0) _monitorPublishInfo.Remove(serverUrl);

                Console.WriteLine($"Deleted Subscription {monitorPublishInfo.Subscription.Id} for the topic {topic}.");
            }

            return true;
        }

        #region private methods

        private void OnMonitorNotification(MonitoredItem monitoreditem, MonitoredItemNotificationEventArgs e)
        {
            VariableNode varNode = (VariableNode)monitoreditem.Subscription.Session.ReadNode(monitoreditem.StartNodeId);
            foreach (var value in monitoreditem.DequeueValues())
            {
                //Console.WriteLine("Got a value");
                var typeManager = new DataTypeManager(monitoreditem.Subscription.Session);
                UaValue opcvalue = typeManager.GetUaValue(varNode, value, false);

                dynamic monitorInfoPair;

                //lock (_monitorPublishInfo)
                //{
                //    monitorInfoPair = _monitorPublishInfo
                //        .SelectMany(pair => pair.Value, (parent, child) => new { ServerUrl = parent.Key, Info = child })
                //        .First(couple => couple.Info.Subscription == monitoreditem.Subscription);
                //}

                var message = $"{monitoreditem.DisplayName}: {opcvalue.Value}";
                //monitorInfoPair.Info.Forward(message);
                _hub.Clients.All.SendAsync("askServerResponse", new MonitorModel() { NodeId = monitoreditem.DisplayName ,value= opcvalue.Value.ToString() });
                //Console.WriteLine(message);


            }
        }

        private static Subscription CreateSubscription(Session session, int publishingInterval, uint maxNotificationPerPublish)
        {
            var sub = new Subscription(session.DefaultSubscription)
            {
                PublishingInterval = publishingInterval,
                MaxNotificationsPerPublish = maxNotificationPerPublish
            };

            if (!session.AddSubscription(sub)) return null;
            sub.Create();
            return sub;

        }

        /// <summary>
        /// This method is called when a OPC UA Service call in a session object returns an error 
        /// </summary>
        /// <param name="serverUrlstring"></param>
        /// <returns></returns>
        private async Task<bool> RestoreSessionAsync(string serverUrlstring)
        {
            lock (_sessions)
            {
                if (_sessions.ContainsKey(serverUrlstring))
                    _sessions.Remove(serverUrlstring);
            }

            Session session;
            try
            {
                return (await GetSessionAsync(serverUrlstring)).IsServerStatusGood();
            }
            catch (Exception)
            {
                return false;
            }
        }

        private async Task<Session> GetSessionAsync(string serverUrl)
        {
            lock (_sessions)
            {
                if (_sessions.ContainsKey(serverUrl)) return _sessions[serverUrl];
            }

            await CheckAndLoadConfiguration();
            EndpointDescription endpointDescription;
            try
            {
                endpointDescription = CoreClientUtils.SelectEndpoint(serverUrl, useSecurity, 15000);
            }
            catch (Exception)
            {
                throw new DataSetNotAvailableException();
            }

            Console.WriteLine("    Selected endpoint uses: {0}",
                endpointDescription.SecurityPolicyUri.Substring(endpointDescription.SecurityPolicyUri.LastIndexOf('#') + 1));

            var endpointConfiguration = EndpointConfiguration.Create(_appConfiguration);

            var endpoint = new ConfiguredEndpoint(endpointDescription.Server, endpointConfiguration);
            endpoint.Update(endpointDescription);

            var s = await Session.Create(_appConfiguration,
                                             endpoint,
                                             true,
                                             false,
                                             _appConfiguration.ApplicationName + "_session",
                                             (uint)_appConfiguration.ClientConfiguration.DefaultSessionTimeout,
                                             null,
                                             null);

            lock (_sessions)
            {
                if (_sessions.ContainsKey(serverUrl))
                    s = _sessions[serverUrl];
                else
                    _sessions.Add(serverUrl, s);
            }

            return s;
        }

        private async Task CheckAndLoadConfiguration()
        {
            if (_appConfiguration == null)
            {
                _appConfiguration = await _application.LoadApplicationConfiguration(false);

                var haveAppCertificate = await _application.CheckApplicationInstanceCertificate(false, 0);
                if (!haveAppCertificate)
                {
                    throw new Exception("Application instance certificate invalid!");
                }

                _appConfiguration.ApplicationUri =
                    Utils.GetApplicationUriFromCertificate(_appConfiguration.SecurityConfiguration.ApplicationCertificate
                        .Certificate);
                if (_appConfiguration.SecurityConfiguration.AutoAcceptUntrustedCertificates)
                {
                    _autoAccept = true;
                }

                _appConfiguration.CertificateValidator.CertificateValidation += CertificateValidator_CertificateValidation;
            }
        }

        private void CertificateValidator_CertificateValidation(CertificateValidator sender, CertificateValidationEventArgs e)
        {
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                e.Accept = _autoAccept;
                Console.WriteLine(_autoAccept ? "Accepted Certificate: {0}" : "Rejected Certificate: {0}",
                    e.Certificate.Subject);
            }
        }

        #endregion
    }
}