namespace API.Exceptions
{
    public class ValueToWriteTypeException : Exception
    {
        public ValueToWriteTypeException() : base() { }

        public ValueToWriteTypeException(string message) : base(message) { }

        public ValueToWriteTypeException(string message, Exception innerException) : base(message, innerException) { }
    }
}
