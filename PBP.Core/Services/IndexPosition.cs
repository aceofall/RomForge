namespace PBP.Core.Services;

public class IndexPosition
{
    public int Minutes { get; set; }

    public int Seconds { get; set; }

    public int Frames { get; set; }

    public static IndexPosition operator +(IndexPosition a, IndexPosition b)
    {
        var frames = a.Frames + b.Frames;
        var framesCarry = 0;

        if (frames >= 75) 
        { 
            framesCarry = frames / 75; 
            frames %= 75; 
        }

        var seconds = a.Seconds + b.Seconds + framesCarry;
        var secondsCarry = 0;

        if (seconds >= 60) 
        {
            secondsCarry = seconds / 60; 
            seconds %= 60; 
        }

        var minutes = a.Minutes + b.Minutes + secondsCarry;

        return new IndexPosition { Minutes = minutes, Seconds = seconds, Frames = frames };
    }

    public static IndexPosition operator +(IndexPosition a, int framesB)
    {
        var frames = a.Frames + framesB;
        var framesCarry = 0;

        if (frames >= 75) 
        { 
            framesCarry = frames / 75; 
            frames %= 75; 
        }

        var seconds = a.Seconds + framesCarry;
        var secondsCarry = 0;

        if (seconds >= 60) 
        { 
            secondsCarry = seconds / 60; 
            seconds %= 60; 
        }

        var minutes = a.Minutes + secondsCarry;

        return new IndexPosition { Minutes = minutes, Seconds = seconds, Frames = frames };
    }
}