using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using OtpNet;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Infrastructure.Mfa;
using Xunit;

namespace ShopFlow.Auth.UnitTests.Mfa;

/// <summary>
/// Sprint-9 U4 — RFC 6238 TOTP wrapper contract. The drift window +
/// malformed-input safety are load-bearing for the MFA verify path.
/// </summary>
public sealed class OtpNetTotpProviderTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);

    private static FakeTimeProvider Clock() => new(FixedNow);

    [Fact]
    public void GenerateSecret_Returns20BytesEachCall()
    {
        var provider = new OtpNetTotpProvider();
        var s1 = provider.GenerateSecret();
        var s2 = provider.GenerateSecret();

        s1.Length.Should().Be(20);
        s2.Length.Should().Be(20);
        s1.Should().NotEqual(s2, "secrets must be cryptographically distinct per call");
    }

    [Fact]
    public void GenerateProvisioningUri_ProducesParseableOtpauthUri()
    {
        var provider = new OtpNetTotpProvider();
        var secret = provider.GenerateSecret();

        var uri = provider.GenerateProvisioningUri(secret, "user@example.com", "ShopFlow WMS");

        uri.Should().StartWith("otpauth://totp/");
        uri.Should().Contain("secret=");
        uri.Should().Contain("digits=6");
        uri.Should().Contain("period=30");
    }

    [Fact]
    public void VerifyOtp_AcceptsCodeForCurrentTimeStep()
    {
        var provider = new OtpNetTotpProvider();
        var secret = provider.GenerateSecret();
        var clock = Clock();

        var totp = new Totp(secret, step: 30, mode: OtpHashMode.Sha1, totpSize: 6);
        var currentCode = totp.ComputeTotp(clock.GetUtcNow().UtcDateTime);

        var result = provider.VerifyOtp(secret, currentCode, clock);

        result.IsValid.Should().BeTrue();
        result.TimeStep.Should().BeGreaterThan(0);
    }

    [Fact]
    public void VerifyOtp_AcceptsCodeFromPreviousTimeStepWithinDriftWindow()
    {
        var provider = new OtpNetTotpProvider();
        var secret = provider.GenerateSecret();
        var clock = Clock();

        var totp = new Totp(secret, step: 30, mode: OtpHashMode.Sha1, totpSize: 6);
        // Code generated for time T-30s
        var earlierCode = totp.ComputeTotp(clock.GetUtcNow().UtcDateTime.AddSeconds(-30));

        var result = provider.VerifyOtp(secret, earlierCode, clock);

        result.IsValid.Should().BeTrue("±1-step drift window must accept T-30s code");
    }

    [Fact]
    public void VerifyOtp_RejectsCodeOutsideDriftWindow()
    {
        var provider = new OtpNetTotpProvider();
        var secret = provider.GenerateSecret();
        var clock = Clock();

        var totp = new Totp(secret, step: 30, mode: OtpHashMode.Sha1, totpSize: 6);
        // Code generated for time T-90s — outside ±1 step
        var farPastCode = totp.ComputeTotp(clock.GetUtcNow().UtcDateTime.AddSeconds(-90));

        var result = provider.VerifyOtp(secret, farPastCode, clock);

        result.IsValid.Should().BeFalse("90-second gap is outside the drift window");
    }

    [Theory]
    [InlineData("000000")]
    [InlineData("abcdef")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("")]
    [InlineData("   ")]
    public void VerifyOtp_MalformedCode_ReturnsFalseNotThrow(string code)
    {
        var provider = new OtpNetTotpProvider();
        var secret = provider.GenerateSecret();
        var clock = Clock();

        var result = provider.VerifyOtp(secret, code, clock);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void VerifyOtp_EmptySecret_ReturnsFalse()
    {
        var provider = new OtpNetTotpProvider();
        var result = provider.VerifyOtp(Array.Empty<byte>(), "123456", Clock());

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void VerifyOtp_NullSecret_ReturnsFalse()
    {
        var provider = new OtpNetTotpProvider();
        var result = provider.VerifyOtp(null!, "123456", Clock());

        result.IsValid.Should().BeFalse();
    }
}
