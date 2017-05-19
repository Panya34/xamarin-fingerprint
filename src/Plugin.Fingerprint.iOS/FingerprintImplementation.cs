﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Foundation;
using LocalAuthentication;
using ObjCRuntime;
using Plugin.Fingerprint.Abstractions;
#if !__MAC__
using UIKit;
#endif

namespace Plugin.Fingerprint
{
    internal class FingerprintImplementation : FingerprintImplementationBase
    {
        private LAContext _context;

        public FingerprintImplementation()
        {
            CreateLaContext();
        }

        protected override async Task<FingerprintAuthenticationResult> NativeAuthenticateAsync(AuthenticationRequestConfiguration authRequestConfig, CancellationToken cancellationToken = new CancellationToken())
        {
            var result = new FingerprintAuthenticationResult();
            SetupContextProperties(authRequestConfig);

            Tuple<bool, NSError> resTuple;
            using (cancellationToken.Register(CancelAuthentication))
            {
                resTuple = await _context.EvaluatePolicyAsync(LAPolicy.DeviceOwnerAuthentication, authRequestConfig.Reason);
            }

            if (resTuple.Item1)
            {
                result.Status = FingerprintAuthenticationResultStatus.Succeeded;
            }
            else
            {
                switch ((LAStatus)(int)resTuple.Item2.Code)
                {
                    case LAStatus.AuthenticationFailed:
                        var description = resTuple.Item2.Description;
                        if (description != null && description.Contains("retry limit exceeded"))
                        {
                            result.Status = FingerprintAuthenticationResultStatus.TooManyAttempts;
                        }
                        else
                        {
                            result.Status = FingerprintAuthenticationResultStatus.Failed;
                        }
                        break;

                    case LAStatus.UserCancel:
                    case LAStatus.AppCancel:
                        result.Status = FingerprintAuthenticationResultStatus.Canceled;
                        break;

                    case LAStatus.UserFallback:
                        result.Status = FingerprintAuthenticationResultStatus.FallbackRequested;
                        break;

                    case LAStatus.TouchIDLockout:
                        result.Status = FingerprintAuthenticationResultStatus.TooManyAttempts;
                        break;

                    default:
                        result.Status = FingerprintAuthenticationResultStatus.UnknownError;
                        break;
                }

                result.ErrorMessage = resTuple.Item2.LocalizedDescription;
            }

            CreateNewContext();
            return result;
        }

        public override async Task<FingerprintAvailability> GetAvailabilityAsync()
        {
            NSError error;

            if (_context == null)
                return FingerprintAvailability.NoApi;

            if (_context.CanEvaluatePolicy(LAPolicy.DeviceOwnerAuthentication, out error))
                return FingerprintAvailability.Available;

            switch ((LAStatus)(int)error.Code)
            {
                case LAStatus.TouchIDNotAvailable:
                    return FingerprintAvailability.NoSensor;
                case LAStatus.TouchIDNotEnrolled:
                case LAStatus.PasscodeNotSet:
                    return FingerprintAvailability.NoFingerprint;
                default:
                    return FingerprintAvailability.Unknown;
            }
        }

        private void SetupContextProperties(AuthenticationRequestConfiguration authRequestConfig)
        {
            if (_context.RespondsToSelector(new Selector("localizedFallbackTitle")))
            {
                _context.LocalizedFallbackTitle = authRequestConfig.FallbackTitle;
            }
#if !__MAC__ // will be included in Cycle 9
            if (_context.RespondsToSelector(new Selector("localizedCancelTitle")))
            {
                _context.LocalizedCancelTitle = authRequestConfig.CancelTitle;
            }
#endif
        }

        private void CancelAuthentication()
        {
            CreateNewContext();
        }

        private void CreateNewContext()
        {
            if (_context != null)
            {
                if (_context.RespondsToSelector(new Selector("invalidate")))
                {
                    _context.Invalidate();
                }
                _context.Dispose();
            }

            CreateLaContext();
        }

        private void CreateLaContext()
        {
            var info = new NSProcessInfo();
#if __MAC__
            var minVersion = new NSOperatingSystemVersion(10, 12, 0);
            if (!info.IsOperatingSystemAtLeastVersion(minVersion))
                return;
#else
			if (!UIDevice.CurrentDevice.CheckSystemVersion(8, 0))
				return;
#endif
            // Check LAContext is not available on iOS7 and below, so check LAContext after checking iOS version.
            if (Class.GetHandle(typeof(LAContext)) == IntPtr.Zero)
                return;

            _context = new LAContext();
        }
    }
}