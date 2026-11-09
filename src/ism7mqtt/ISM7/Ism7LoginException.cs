using System;
using ism7mqtt.ISM7.Protocol;

namespace ism7mqtt
{
    /// <summary>
    /// The ism7 did not accept the login. A rejected login (<c>invalidCredentials</c>, <c>error</c>) is never retried:
    /// trying again would not help and could get the client locked out. <c>busy</c> and <c>pending</c> are transient
    /// (e.g. the ism7 still holds the previous session, see #245) and are retried like a connection error.
    /// </summary>
    public class Ism7LoginException : Exception
    {
        public Ism7LoginException(LoginState state)
            : base($"invalid login state '{state}'")
        {
            State = state;
        }

        public LoginState State { get; }

        /// <summary>The ism7 is busy or still holds a previous session - worth another attempt.</summary>
        public bool IsTransient => State == LoginState.busy || State == LoginState.pending;
    }
}
