using API.Exceptions;
using API.Models.DataSet;
using API.Models.OPCUA;
using API.Models.OptionsModels;
using API.OPCUALayer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Opc.Ua;
using System.Net;
using StatusCodes = Opc.Ua.StatusCodes;

namespace API.Controllers
{
    [AllowAnonymous]
    public class OpcUaController:BaseApiController
    {
        private readonly OPCUAServers[] _uaServers;
        private readonly IUaClientSingleton _uaClient;

        /// <summary>
        /// Constructor an ApiController which controls the servers and client 
        /// </summary>
        /// <param name="servers">options</param>
        /// <param name="UAClient">server </param>

        public OpcUaController(IOptions<OPCUAServersOptions> servers, IUaClientSingleton UAClient)
        {
            this._uaServers = servers.Value.Servers;
            for (int i = 0; i < _uaServers.Length; i++) _uaServers[i].Id = i;

            this._uaClient = UAClient;
        }
        /// <summary>
        /// select endpoints
        /// </summary>
        /// <param name="serverUrl"></param>
        /// <returns>an action</returns>
        [HttpPost]
        [Route("get-endpoints")]
        public IActionResult GetEndpoints([FromBody] string serverUrl)
        {
            return Ok(_uaClient.GetEndpoint(serverUrl));
        }
        /// <summary>
        /// Disconnect to the server
        /// </summary>
        /// <returns>an action</returns>
        [HttpGet]
        [Route("disconnect")]
        public IActionResult DisconnectServer()
        {
            _uaClient.ServerDisconenctAsync(_uaServers[0].Url);
            return Ok("OK");
        }
        /// <summary>
        /// Get datasets
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("data-sets")]
        public IActionResult GetDataSets()
        {
            return Ok(_uaServers);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="serverUrl"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("data-sets/route")]
        public async Task<IActionResult> GetDataSetsRoot([FromBody] Endpoints serverUrl)
        {
            _uaServers[0].Url = serverUrl.serverUrl;
            _uaClient.SetUseSecurity(serverUrl.useSecurity);
            if (!(await _uaClient.IsServerAvailable(serverUrl.serverUrl)))
                return StatusCode(500, "Data Set For " + serverUrl.serverUrl + " NotAvailable");
            Tree tree;
            tree = await _uaClient.GetRootNode(serverUrl.serverUrl, serverUrl.useSecurity);
            if (tree.currentView[0].children == true)
            {
                tree = await _uaClient.GetChildren(serverUrl.serverUrl, tree.currentView[0].id.ToString());
            }

            return Ok(tree);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="node_id"></param>
        /// <returns></returns>
        [HttpPost("data-sets/route/expand")]
        public async Task<IActionResult> GetExpandNode([FromBody] string node_id)
        {
            //var decodedNodeId = WebUtility.UrlDecode(node_id);
            string serverUrl = _uaServers[0].Url;

            if (!(await _uaClient.IsServerAvailable(serverUrl)))
                return StatusCode(500, "Data Set For" + serverUrl + " NotAvailable");
            Tree tree;
                tree = await _uaClient.GetChildren(serverUrl, node_id);
            return Ok(tree); ;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="node_id"></param>
        /// <param name="serverUrl"></param>
        /// <returns></returns>
        [HttpPost("data-sets/browsing-tree")]
        public async Task<IActionResult> BrowserModel([FromBody] string node_id)
        {
            var serverUrl = _uaServers[0].Url;
            if (!(await _uaClient.IsServerAvailable(serverUrl)))
                return StatusCode(500, "Data Set For" + serverUrl + " NotAvailable");
            var result = new JObject();
            return Ok(result.ToString());

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="node_id"></param>
        /// <returns></returns>
        [HttpGet("data-sets/nodes/{node_id:regex(^\\d+-(?:(\\d+)|(.+))$)?}")] ///Example: {node_id:2-12331  }
        public async Task<IActionResult> GetNode(string node_id = "0-85")
        {
            var serverUrl = _uaServers[0].Url;
            if (!(await _uaClient.IsServerAvailable(serverUrl)))
                return StatusCode(500, "Data Set For" + serverUrl + " NotAvailable");

            var decodedNodeId = WebUtility.UrlDecode(node_id);

            var result = new JObject();

            try
            {
                var sourceNode = await _uaClient.ReadNodeAsync(serverUrl, decodedNodeId);
                result["node_id"] = decodedNodeId;
                result["name"] = sourceNode.DisplayName.Text;

                switch (sourceNode.NodeClass)
                {
                    case NodeClass.Method:
                        result["type"] = "method";
                        break;
                    case NodeClass.Variable:
                        result["type"] = "variable";
                        var varNode = (VariableNode)sourceNode;
                        var uaValue = await _uaClient.ReadUaValueAsync(serverUrl, varNode);
                        result["value"] = uaValue.Value;
                        result["value-schema"] = JObject.Parse(uaValue.Schema.ToString());
                        result["status"] = uaValue.StatusCode?.ToString() ?? "";
                        result["deadBand"] = await _uaClient.GetDeadBandAsync(serverUrl, varNode);
                        result["minimumSamplingInterval"] = varNode.MinimumSamplingInterval;
                        break;
                    case NodeClass.Object:
                        result["type"] = await _uaClient.IsFolderTypeAsync(serverUrl, decodedNodeId) ? "folder" : "object";
                        break;
                }

                var linkedNodes = new JArray();
                var refDescriptions = await _uaClient.BrowseAsync(serverUrl, decodedNodeId);
                foreach (var rd in refDescriptions)
                {
                    var refTypeNode = await _uaClient.ReadNodeAsync(serverUrl, rd.ReferenceTypeId);
                    var targetNode = new JObject
                    {
                        ["node-id"] = rd.PlatformNodeId,
                        ["name"] = rd.DisplayName
                    };


                    switch (rd.NodeClass)
                    {
                        case NodeClass.Variable:
                            targetNode["Type"] = "variable";
                            break;
                        case NodeClass.Method:
                            targetNode["Type"] = "method";
                            break;
                        case NodeClass.Object:
                            targetNode["Type"] = await _uaClient.IsFolderTypeAsync(serverUrl, rd.PlatformNodeId)
                                ? "folder"
                                : "object";
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    targetNode["relationship"] = refTypeNode.DisplayName.Text;

                    linkedNodes.Add(targetNode);
                }

                result["edges"] = linkedNodes;
            }
            catch (ServiceResultException exc)
            {
                switch (exc.StatusCode)
                {
                    case StatusCodes.BadNodeIdUnknown:
                        return NotFound(new
                        {
                            error = "Wrong ID: There is no Resource with ID " + decodedNodeId
                        });
                    case StatusCodes.BadNodeIdInvalid:
                        return BadRequest(new
                        {
                            error = "Provided ID is invalid"
                        });
                    case StatusCodes.BadSessionIdInvalid:
                    case StatusCodes.BadSessionClosed:
                    case StatusCodes.BadSessionNotActivated:
                    case StatusCodes.BadTooManySessions:
                        return StatusCode(500, new
                        {
                            error = "Connection Lost"
                        });
                    default:
                        return StatusCode(500, new
                        {
                            error = exc.Message
                        });
                }
            }
            catch (DataSetNotAvailableException)
            {
                return StatusCode(500, "Data Set For" + serverUrl + " NotAvailable");
            }
            return Ok(result.ToString());

        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="node_id"></param>
        /// <param name="a"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("data-sets/nodes/{node_id:regex(^\\d+-(?:(\\d+)|(.+))$)?}")]
        public async Task<IActionResult> PostNode(string node_id, [FromBody] string a)
        {
            VariableState state = new VariableState();
            if (a == "true") state.Value = true;
            else if (a == "false") state.Value = false;
            else if (double.TryParse(a, out double value))
            {
                state.Value = value;
            };

            if (state == null || !state.IsValid)
                return BadRequest(new
                {
                    error = "Insert a valid state for a Variable Node."
                });
            var serverUrl = _uaServers[0].Url;
            if (!(await _uaClient.IsServerAvailable(serverUrl)))
                return StatusCode(500, new
                {
                    error = "Data Set For" + serverUrl + " NotAvailable"
                });

            var decodedNodeId = WebUtility.UrlDecode(node_id);

            Node sourceNode;
            try
            {
                sourceNode = await _uaClient.ReadNodeAsync(serverUrl, decodedNodeId);
            }
            catch (ServiceResultException exc)
            {
                switch (exc.StatusCode)
                {
                    case StatusCodes.BadNodeIdUnknown:
                        return NotFound(new
                        {
                            error = "Wrong ID: There is no Resource with ID " + decodedNodeId
                        });
                    case StatusCodes.BadNodeIdInvalid:
                        return BadRequest(new
                        {
                            error = "Provided ID is invalid"
                        });
                    case StatusCodes.BadSessionIdInvalid:
                    case StatusCodes.BadSessionClosed:
                    case StatusCodes.BadSessionNotActivated:
                    case StatusCodes.BadTooManySessions:
                        return StatusCode(500, new
                        {
                            error = "Connection Lost"
                        });
                    default:
                        return StatusCode(500, new
                        {
                            error = exc.Message
                        });
                }
            }
            catch (DataSetNotAvailableException)
            {
                return StatusCode(500, new
                {
                    error = "Data Set For" + serverUrl + " NotAvailable"
                });
            }

            if (sourceNode.NodeClass != NodeClass.Variable)
                return BadRequest(new
                {
                    error = "There is no Value for the Node specified by the NodeId " + node_id
                });

            VariableNode variableNode = (VariableNode)sourceNode;

            try
            {
                await _uaClient.WriteNodeValueAsync(serverUrl, variableNode, state);
            }
            catch (ValueToWriteTypeException exc)
            {
                return BadRequest(new
                {
                    error = exc.Message
                });
            }
            catch (NotImplementedException exc)
            {
                return StatusCode(500, new
                {
                    error = exc.Message
                });
            }
            catch (ServiceResultException exc)
            {
                switch (exc.StatusCode)
                {
                    case (StatusCodes.BadTypeMismatch):
                        return BadRequest(new
                        {
                            error = "Wrong Type - Check data and try again"
                        });
                    case StatusCodes.BadSessionIdInvalid:
                    case StatusCodes.BadSessionClosed:
                    case StatusCodes.BadSessionNotActivated:
                    case StatusCodes.BadTooManySessions:
                        return StatusCode(500, new
                        {
                            error = "Connection Lost"
                        });
                    default:
                        return BadRequest(new
                        {
                            error = exc.Message
                        });
                }

            }
            return Ok(true);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="monitorParams"></param>
        /// <returns></returns>
        [HttpPost("data-sets/monitor")]
        public async Task<IActionResult> Monitor([FromBody] MonitorParams monitorParams)
        {

            if (monitorParams == null || !monitorParams.IsValid())
            {
                return BadRequest(new
                {
                    error = "Bad parameters format."
                });
            }

            if (!monitorParams.IsTelemetryProtocolSupported())
            {
                return BadRequest(new
                {
                    error = "Telemetry protocol provided in the broker url is not supported by the platform."
                });
            }

            foreach (var monitorableNode in monitorParams.MonitorableNodes)
            {
                if (!new List<string> { "Absolute", "Percent", "None" }.Contains(monitorableNode.DeadBand))
                {
                    return BadRequest(new
                    {
                        error = $"Value not allowed for DeadBand parameter. Found '{monitorableNode.DeadBand}'"
                    });
                }
            }

            var serverUrl = _uaServers[0].Url;
            if (!(await _uaClient.IsServerAvailable(serverUrl)))
                return StatusCode(500, "Data Set " + 0 + " NotAvailable");

            bool[] results;
            try
            {
                results = await _uaClient.CreateMonitoredItemsAsync(serverUrl,
                    monitorParams.MonitorableNodes,
                    monitorParams.BrokerUrl,
                    monitorParams.Topic);
            }
            catch (ServiceResultException exc)
            {
                switch (exc.StatusCode)
                {
                    case StatusCodes.BadNodeIdUnknown:
                        return NotFound("There is no node with the specified Node Id");
                    case StatusCodes.BadNodeIdInvalid:
                        return BadRequest("Provided Node Id is invalid");
                    case StatusCodes.BadSessionIdInvalid:
                    case StatusCodes.BadSessionClosed:
                    case StatusCodes.BadSessionNotActivated:
                    case StatusCodes.BadTooManySessions:
                        return StatusCode(500, new
                        {
                            error = "Connection Lost"
                        });
                    default:
                        return StatusCode(500, exc.Message);
                }
            }
            catch (DataSetNotAvailableException)
            {
                return StatusCode(500, "Data Set For" + serverUrl + " NotAvailable");
            }


            return Ok(new
            {
                results
            });
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="stopMonitorParams"></param>
        /// <returns></returns>
        [HttpPost("data-sets/stop-monitor")]
        public async Task<IActionResult> StopMonitor([FromBody] StopMonitorParams stopMonitorParams)
        {
            if (stopMonitorParams == null || !stopMonitorParams.IsValid())
            {
                return BadRequest(new
                {
                    error = "Bad parameters format."
                });
            }

            var serverUrl = _uaServers[0].Url;
            var result = await _uaClient.DeleteMonitoringPublish(serverUrl, stopMonitorParams.BrokerUrl,
                    stopMonitorParams.Topic);

            if (result)
            {
                return Ok();
            }

            return BadRequest(new
            {
                error = $"An error occurred trying to delete the topic {stopMonitorParams.Topic} on broker {stopMonitorParams.BrokerUrl}. " +
                        $"Maybe there is no current monitoring for such parameters or an internal error occurred in the Data Set."
            });
        }

    }
}
