using System.Text.RegularExpressions;
using API.Exceptions;
using Opc.Ua;

namespace API.OPC_UA_Layer
{
    public static class PlatformUtils
    {
        public static NodeId ParsePlatformNodeIdString(string str)
        {
            const string pattern = @"^(\d+)-(?:(\d+)|(\S+))$";
            var match = Regex.Match(str, pattern);
            var isString = match.Groups[3].Length != 0;
            var isNumeric = match.Groups[2].Length != 0;

            var idStr = (isString) ? $"s={match.Groups[3]}" : $"i={match.Groups[2]}";
            var builtStr = $"ns={match.Groups[1]};" + idStr;
            NodeId nodeId = null;
            try
            {
                nodeId = new NodeId(builtStr);
            }
            catch (ServiceResultException exc)
            {
                switch (exc.StatusCode)
                {
                    case Opc.Ua.StatusCodes.BadNodeIdInvalid:
                        throw new ValueToWriteTypeException("Wrong Type Error: String is not formatted as expected (number-yyy where yyy can be string or number or guid)");
                    default:
                        throw new ValueToWriteTypeException(exc.Message);
                }
            }
            
            return nodeId;
        }
    }
}