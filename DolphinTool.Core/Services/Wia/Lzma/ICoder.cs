namespace DolphinTool.Core.Services.Wia.Lzma;

class DataErrorException : ApplicationException
{
	public DataErrorException(): base("Data Error") { }
}

class InvalidParamException : ApplicationException
{
	public InvalidParamException(): base("Invalid Parameter") { }
}

public interface ICodeProgress
{
	void SetProgress(Int64 inSize, Int64 outSize);
};

public interface ISetDecoderProperties
{
	void SetDecoderProperties(byte[] properties);
}