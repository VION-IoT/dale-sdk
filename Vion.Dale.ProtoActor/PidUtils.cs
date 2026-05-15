using Proto;

namespace Vion.Dale.ProtoActor
{
    public static class PidUtils
    {
        public static PID FromName(string name)
        {
            return new PID("nonhost", name);
        }
    }
}