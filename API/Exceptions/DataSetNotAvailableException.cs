namespace API.Exceptions
{
    public class DataSetNotAvailableException : Exception
    {
        public DataSetNotAvailableException() : base() { }

        public DataSetNotAvailableException(string message) : base(message) { }

        public DataSetNotAvailableException(string message, Exception innerException) : base(message, innerException) { }
    }
}
