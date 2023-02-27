﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

#nullable disable

namespace Microsoft.Build.Framework
{
    // TODO: this should be unified with Shared\ErrorUtilities.cs, but it is hard to untangle everything
    //       because some of the errors there will use localized resources from different assemblies,
    //       which won't be referenceable in Framework.

    internal class FrameworkErrorUtilities
    {
        /// <summary>
        /// Emergency escape hatch. If a customer hits a bug in the shipped product causing an internal exception,
        /// and fortuitously it happens that ignoring the VerifyThrow allows execution to continue in a reasonable way,
        /// then we can give them this undocumented environment variable as an immediate workaround.
        /// </summary>
        private static readonly bool s_throwExceptions = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MSBUILDDONOTTHROWINTERNAL"));

        /// <summary>
        /// This method should be used in places where one would normally put
        /// an "assert". It should be used to validate that our assumptions are
        /// true, where false would indicate that there must be a bug in our
        /// code somewhere. This should not be used to throw errors based on bad
        /// user input or anything that the user did wrong.
        /// </summary>
        /// <param name="condition"></param>
        /// <param name="unformattedMessage"></param>
        internal static void VerifyThrow(
            bool condition,
            string unformattedMessage)
        {
            if (!condition)
            {
                // PERF NOTE: explicitly passing null for the arguments array
                // prevents memory allocation
                ThrowInternalError(unformattedMessage, null, null);
            }
        }

        /// <summary>
        /// Helper to throw an InternalErrorException when the specified parameter is null.
        /// This should be used ONLY if this would indicate a bug in MSBuild rather than
        /// anything caused by user action.
        /// </summary>
        /// <param name="parameter">The value of the argument.</param>
        /// <param name="parameterName">Parameter that should not be null</param>
        internal static void VerifyThrowInternalNull(object parameter, string parameterName)
        {
            if (parameter == null)
            {
                ThrowInternalError("{0} unexpectedly null", innerException: null, args: parameterName);
            }
        }

        /// <summary>
        /// Throws InternalErrorException.
        /// This is only for situations that would mean that there is a bug in MSBuild itself.
        /// </summary>
        internal static void ThrowInternalError(string message, Exception innerException, params object[] args)
        {
            if (s_throwExceptions)
            {
                throw new InternalErrorException(string.Format(message, args), innerException);
            }
        }
    }
}
