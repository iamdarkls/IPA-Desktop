using System;
namespace IpaDesktop;
/// <summary>Limits only the monitored operation; downloads and other commands retain their own policies.</summary>
public sealed record ProcessRunPolicy(TimeSpan IdleTimeout, Func<string,Exception> ReadFailure, Func<Exception> IdleFailure);
