namespace AndroidSideloader.Sideloader;

public class ProcessOutput
{
    public string Output;
    public string Error;
    public int ExitCode;

    public ProcessOutput(string output = "", string error = "", int exitCode = 0)
    {
        Output = output;
        Error = error;
        ExitCode = exitCode;
    }

    public static ProcessOutput operator +(ProcessOutput a, ProcessOutput b)
    {
        return new ProcessOutput(a.Output + b.Output, a.Error + b.Error);
    }
}