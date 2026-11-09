using System;
using ism7mqtt.ISM7.Protocol;

namespace ism7mqtt
{
    /// <summary>
    /// The ism7 rejected the login (e.g. a wrong password). Trying again would not help and could get the
    /// client locked out, so this is never retried.
    /// </summary>
    public class Ism7LoginException : Exception
    {
        public Ism7LoginException(LoginState state)
            : base($"invalid login state '{state}'")
        {
            State = state;
        }

        public LoginState State { get; }
    }
}
