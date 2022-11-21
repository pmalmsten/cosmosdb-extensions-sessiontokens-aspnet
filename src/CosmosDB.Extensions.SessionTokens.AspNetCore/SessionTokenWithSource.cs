namespace CosmosDB.Extensions.SessionTokens.AspNetCore;

public readonly record struct SessionTokenWithSource(
    SessionTokenSource Source, string SessionToken)
{
    public SessionTokenWithSource ChooseTokenToKeepBySourcePriority(SessionTokenWithSource newerToken)
    {
        // If the incoming token is from a write, take it.
        if (newerToken.Source == SessionTokenSource.FromWrite)
        {
            return newerToken;
        }

        // Otherwise, if this token is from a write, keep this token.
        if (Source == SessionTokenSource.FromWrite)
        {
            return this;
        }

        // Neither token is for a write - so take the newer one.
        return newerToken;
    }
}

public enum SessionTokenSource
{
    FromIncomingRequest,
    FromRead,
    FromWrite
}