using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using ShopFlow.Auth.Domain;
using ShopFlow.Auth.Domain.Entities;
using ShopFlow.Auth.Domain.Events;
using Xunit;

namespace ShopFlow.Auth.UnitTests.Domain;

/// <summary>
/// Sprint-8 U1 — User aggregate behaviour contract. Pins factory validation
/// + the named mutation methods (UpdatePassword, SetRole, Deactivate,
/// RecordLogin) + the 3 domain events the aggregate raises. The
/// Application layer / IPasswordHasher / IUserRepository wrap this
/// aggregate; none of those concerns leak into the tests here.
/// </summary>
public sealed class UserTests
{
    private const string ValidHash = "$argon2id$v=19$m=65536,t=4,p=4$c2FsdA$aGFzaA";

    [Fact]
    public void Create_HappyPath_SetsFieldsAndRaisesUserCreatedEvent()
    {
        var user = User.Create("Operator@example.com", ValidHash, UserRole.Owner);

        user.Email.Should().Be("operator@example.com"); // normalized to lower
        user.PasswordHash.Should().Be(ValidHash);
        user.Role.Should().Be(UserRole.Owner);
        user.IsActive.Should().BeTrue();
        user.LastLoginAt.Should().BeNull();

        user.DomainEvents.Should().HaveCount(1);
        user.DomainEvents[0].Should().BeOfType<UserCreatedEvent>();
        var evt = (UserCreatedEvent)user.DomainEvents[0];
        evt.UserId.Should().Be(user.Id);
        evt.Email.Should().Be("operator@example.com");
        evt.Role.Should().Be(UserRole.Owner);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_RejectsEmptyEmail(string? email)
    {
        var act = () => User.Create(email!, ValidHash, UserRole.Owner);
        act.Should().Throw<ArgumentException>().Where(e => e.ParamName == "email");
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("missing-at-sign.com")]
    [InlineData("user@")]
    [InlineData("@example.com")]
    [InlineData("user@example")]
    public void Create_RejectsMalformedEmail(string email)
    {
        var act = () => User.Create(email, ValidHash, UserRole.Owner);
        act.Should().Throw<ArgumentException>().Where(e => e.ParamName == "email");
    }

    [Fact]
    public void Create_RejectsEmailLongerThan254Chars()
    {
        var longLocal = new string('a', 250);
        var email = $"{longLocal}@example.com"; // > 254 total
        var act = () => User.Create(email, ValidHash, UserRole.Owner);
        act.Should().Throw<ArgumentException>().Where(e => e.ParamName == "email");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void Create_RejectsEmptyPasswordHash(string? hash)
    {
        var act = () => User.Create("user@example.com", hash!, UserRole.Owner);
        act.Should().Throw<ArgumentException>().Where(e => e.ParamName == "passwordHash");
    }

    [Fact]
    public void Create_RejectsUndefinedRoleEnumValue()
    {
        var act = () => User.Create("user@example.com", ValidHash, (UserRole)999);
        act.Should().Throw<ArgumentException>().Where(e => e.ParamName == "role");
    }

    [Theory]
    [InlineData(UserRole.Owner)]
    [InlineData(UserRole.Picker)]
    [InlineData(UserRole.Dispatcher)]
    public void Create_AcceptsEachDefinedRole(UserRole role)
    {
        var user = User.Create("user@example.com", ValidHash, role);
        user.Role.Should().Be(role);
    }

    [Fact]
    public void UpdatePassword_ChangesHashAndRaisesEvent()
    {
        var user = User.Create("user@example.com", ValidHash, UserRole.Owner);
        user.ClearDomainEvents(); // drop the Create event so we observe Update in isolation
        const string newHash = "$argon2id$v=19$m=65536,t=4,p=4$bmV3$bmV3aGFzaA";

        user.UpdatePassword(newHash);

        user.PasswordHash.Should().Be(newHash);
        user.UpdatedAt.Should().NotBeNull();
        user.DomainEvents.Should().ContainSingle(e => e is UserPasswordChangedEvent);
        var evt = (UserPasswordChangedEvent)user.DomainEvents[0];
        evt.UserId.Should().Be(user.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void UpdatePassword_RejectsEmptyHash(string? hash)
    {
        var user = User.Create("user@example.com", ValidHash, UserRole.Owner);
        var act = () => user.UpdatePassword(hash!);
        act.Should().Throw<ArgumentException>().Where(e => e.ParamName == "newPasswordHash");
    }

    [Fact]
    public void SetRole_ChangesRoleAndRaisesEvent()
    {
        var user = User.Create("user@example.com", ValidHash, UserRole.Owner);
        user.ClearDomainEvents();

        user.SetRole(UserRole.Picker);

        user.Role.Should().Be(UserRole.Picker);
        user.UpdatedAt.Should().NotBeNull();
        user.DomainEvents.Should().ContainSingle(e => e is UserRoleChangedEvent);
        var evt = (UserRoleChangedEvent)user.DomainEvents[0];
        evt.UserId.Should().Be(user.Id);
        evt.FromRole.Should().Be(UserRole.Owner);
        evt.ToRole.Should().Be(UserRole.Picker);
    }

    [Fact]
    public void SetRole_NoOpWhenSameRole_NoEventAndNoUpdatedAtBump()
    {
        var user = User.Create("user@example.com", ValidHash, UserRole.Owner);
        user.ClearDomainEvents();
        var before = user.UpdatedAt;

        user.SetRole(UserRole.Owner);

        user.Role.Should().Be(UserRole.Owner);
        user.UpdatedAt.Should().Be(before); // unchanged
        user.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void SetRole_RejectsUndefinedRoleEnumValue()
    {
        var user = User.Create("user@example.com", ValidHash, UserRole.Owner);
        var act = () => user.SetRole((UserRole)999);
        act.Should().Throw<ArgumentException>().Where(e => e.ParamName == "newRole");
    }

    [Fact]
    public void Deactivate_FlipsIsActiveAndBumpsUpdatedAt()
    {
        var user = User.Create("user@example.com", ValidHash, UserRole.Owner);
        user.ClearDomainEvents();

        user.Deactivate();

        user.IsActive.Should().BeFalse();
        user.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void Deactivate_IsIdempotent_DoesNotBumpUpdatedAtAgain()
    {
        var user = User.Create("user@example.com", ValidHash, UserRole.Owner);
        user.Deactivate();
        var firstUpdate = user.UpdatedAt;

        user.Deactivate(); // again

        user.IsActive.Should().BeFalse();
        user.UpdatedAt.Should().Be(firstUpdate); // unchanged
    }

    [Fact]
    public void RecordLogin_SetsLastLoginAt()
    {
        var user = User.Create("user@example.com", ValidHash, UserRole.Owner);
        user.LastLoginAt.Should().BeNull();
        var before = DateTime.UtcNow.AddSeconds(-1);

        user.RecordLogin();

        user.LastLoginAt.Should().NotBeNull();
        user.LastLoginAt.Should().BeAfter(before);
    }

    [Fact]
    public void RecordLogin_DoesNotRaiseDomainEvent()
    {
        var user = User.Create("user@example.com", ValidHash, UserRole.Owner);
        user.ClearDomainEvents();

        user.RecordLogin();

        user.DomainEvents.Should().BeEmpty();
    }

    // -------- Sprint-9 lockout state machine --------

    private static readonly TimeSpan FifteenMin = TimeSpan.FromMinutes(15);
    private const int Five = 5;

    [Fact]
    public void Create_OwnerRole_DefaultsMfaRequiredTrue()
    {
        var owner = User.Create("o@example.com", ValidHash, UserRole.Owner);
        owner.MfaRequired.Should().BeTrue();
        owner.MfaEnrolled.Should().BeFalse();
    }

    [Theory]
    [InlineData(UserRole.Picker)]
    [InlineData(UserRole.Dispatcher)]
    public void Create_NonOwnerRole_DefaultsMfaRequiredFalse(UserRole role)
    {
        var user = User.Create("u@example.com", ValidHash, role);
        user.MfaRequired.Should().BeFalse();
        user.MfaEnrolled.Should().BeFalse();
    }

    [Fact]
    public void RegisterFailedLogin_AttemptsBelowThreshold_ReturnFalse()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero));
        var user = User.Create("user@example.com", ValidHash, UserRole.Picker);

        for (var i = 1; i <= Five - 1; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(10));
            var triggered = user.RegisterFailedLogin(clock, Five, FifteenMin, FifteenMin);
            triggered.Should().BeFalse($"attempt {i} of {Five}");
        }

        user.FailedLoginCount.Should().Be(4);
        user.LockedUntil.Should().BeNull();
    }

    [Fact]
    public void RegisterFailedLogin_FifthAttemptInWindow_TriggersLockoutAndRaisesEvent()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero));
        var user = User.Create("user@example.com", ValidHash, UserRole.Picker);
        user.ClearDomainEvents();

        bool triggered = false;
        for (var i = 1; i <= Five; i++)
        {
            triggered = user.RegisterFailedLogin(clock, Five, FifteenMin, FifteenMin);
            clock.Advance(TimeSpan.FromSeconds(30));
        }

        triggered
            .Should()
            .BeTrue("the 5th attempt within the sliding window must trip the lockout boundary");
        user.FailedLoginCount.Should().Be(5);
        user.LockedUntil.Should().NotBeNull();
        user.DomainEvents.Should().ContainSingle(e => e is UserLockedEvent);
        var evt = (UserLockedEvent)user.DomainEvents[0];
        evt.UserId.Should().Be(user.Id);
        evt.FailedLoginCount.Should().Be(5);
        evt.LockedUntil.Should().Be(user.LockedUntil!.Value);
    }

    [Fact]
    public void RegisterFailedLogin_4FailuresThenWindowExpiry_ResetsCounter()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero));
        var user = User.Create("user@example.com", ValidHash, UserRole.Picker);

        for (var i = 0; i < 4; i++)
        {
            user.RegisterFailedLogin(clock, Five, FifteenMin, FifteenMin);
            clock.Advance(TimeSpan.FromMinutes(1));
        }
        user.FailedLoginCount.Should().Be(4);

        // 16 minutes after the last failure → window expired → next attempt resets counter
        clock.Advance(TimeSpan.FromMinutes(16));
        var triggered = user.RegisterFailedLogin(clock, Five, FifteenMin, FifteenMin);

        triggered.Should().BeFalse();
        user.FailedLoginCount.Should().Be(1, "sliding window expired → counter restarts at 1");
        user.LockedUntil.Should().BeNull();
    }

    [Fact]
    public void RegisterFailedLogin_AlreadyLocked_DoesNotExtendLockout()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero));
        var user = User.Create("user@example.com", ValidHash, UserRole.Picker);

        // Trip the lockout
        for (var i = 0; i < 5; i++)
        {
            user.RegisterFailedLogin(clock, Five, FifteenMin, FifteenMin);
        }
        var originalLockedUntil = user.LockedUntil;
        user.ClearDomainEvents();

        // 6th attempt while still locked
        clock.Advance(TimeSpan.FromMinutes(1));
        var triggered = user.RegisterFailedLogin(clock, Five, FifteenMin, FifteenMin);

        triggered.Should().BeFalse();
        user.LockedUntil.Should()
            .Be(
                originalLockedUntil,
                "lockout window must not be extended by attempts while locked"
            );
        user.DomainEvents.Should().BeEmpty("no second UserLockedEvent");
    }

    [Fact]
    public void RegisterFailedLogin_RejectsInvalidParameters()
    {
        var clock = new FakeTimeProvider();
        var user = User.Create("user@example.com", ValidHash, UserRole.Picker);

        ((Action)(() => user.RegisterFailedLogin(null!, Five, FifteenMin, FifteenMin)))
            .Should()
            .Throw<ArgumentNullException>();
        ((Action)(() => user.RegisterFailedLogin(clock, 0, FifteenMin, FifteenMin)))
            .Should()
            .Throw<ArgumentOutOfRangeException>();
        ((Action)(() => user.RegisterFailedLogin(clock, Five, TimeSpan.Zero, FifteenMin)))
            .Should()
            .Throw<ArgumentOutOfRangeException>();
        ((Action)(() => user.RegisterFailedLogin(clock, Five, FifteenMin, TimeSpan.Zero)))
            .Should()
            .Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ResetFailures_ClearsCountAndTimestamp()
    {
        var clock = new FakeTimeProvider();
        var user = User.Create("user@example.com", ValidHash, UserRole.Picker);
        user.RegisterFailedLogin(clock, Five, FifteenMin, FifteenMin);
        user.RegisterFailedLogin(clock, Five, FifteenMin, FifteenMin);
        user.FailedLoginCount.Should().Be(2);
        user.LastFailedLoginAt.Should().NotBeNull();

        user.ResetFailures();

        user.FailedLoginCount.Should().Be(0);
        user.LastFailedLoginAt.Should().BeNull();
    }

    [Fact]
    public void ResetFailures_IsIdempotentWhenAlreadyClean()
    {
        var user = User.Create("user@example.com", ValidHash, UserRole.Picker);
        var before = user.UpdatedAt;
        user.ResetFailures();
        user.UpdatedAt.Should().Be(before);
    }

    [Fact]
    public void RecordLogin_ResetsLockoutCounter()
    {
        var clock = new FakeTimeProvider();
        var user = User.Create("user@example.com", ValidHash, UserRole.Picker);
        user.RegisterFailedLogin(clock, Five, FifteenMin, FifteenMin);
        user.FailedLoginCount.Should().Be(1);

        user.RecordLogin();

        user.FailedLoginCount.Should().Be(0);
        user.LastFailedLoginAt.Should().BeNull();
        user.LastLoginAt.Should().NotBeNull();
    }

    [Fact]
    public void Unlock_ClearsLockedUntilAndCounter()
    {
        var clock = new FakeTimeProvider();
        var user = User.Create("user@example.com", ValidHash, UserRole.Picker);
        for (var i = 0; i < 5; i++)
        {
            user.RegisterFailedLogin(clock, Five, FifteenMin, FifteenMin);
        }
        user.LockedUntil.Should().NotBeNull();

        user.Unlock();

        user.LockedUntil.Should().BeNull();
        user.FailedLoginCount.Should().Be(0);
        user.LastFailedLoginAt.Should().BeNull();
    }

    [Fact]
    public void Unlock_IsIdempotentWhenNotLocked()
    {
        var user = User.Create("user@example.com", ValidHash, UserRole.Picker);
        var before = user.UpdatedAt;
        user.Unlock();
        user.UpdatedAt.Should().Be(before);
    }

    // -------- Sprint-9 MFA state machine --------

    [Fact]
    public void RequireMfa_FlipsFlag()
    {
        var user = User.Create("u@example.com", ValidHash, UserRole.Picker);
        user.MfaRequired.Should().BeFalse();

        user.RequireMfa(true);

        user.MfaRequired.Should().BeTrue();
        user.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void RequireMfa_IsIdempotent()
    {
        var user = User.Create("u@example.com", ValidHash, UserRole.Owner);
        user.MfaRequired.Should().BeTrue();
        var before = user.UpdatedAt;

        user.RequireMfa(true);

        user.UpdatedAt.Should().Be(before);
    }

    [Fact]
    public void MarkMfaEnrolled_FlipsFlagAndRaisesEvent()
    {
        var user = User.Create("u@example.com", ValidHash, UserRole.Owner);
        user.ClearDomainEvents();

        user.MarkMfaEnrolled();

        user.MfaEnrolled.Should().BeTrue();
        user.UpdatedAt.Should().NotBeNull();
        user.DomainEvents.Should().ContainSingle(e => e is UserMfaEnrolledEvent);
        ((UserMfaEnrolledEvent)user.DomainEvents[0]).UserId.Should().Be(user.Id);
    }

    [Fact]
    public void MarkMfaEnrolled_IsIdempotentWhenAlreadyEnrolled()
    {
        var user = User.Create("u@example.com", ValidHash, UserRole.Owner);
        user.MarkMfaEnrolled();
        var before = user.UpdatedAt;
        user.ClearDomainEvents();

        user.MarkMfaEnrolled();

        user.UpdatedAt.Should().Be(before);
        user.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void MarkMfaDisabled_RaisesEventWithByOwnerActionFalse()
    {
        var user = User.Create("u@example.com", ValidHash, UserRole.Picker);
        user.MarkMfaEnrolled();
        user.ClearDomainEvents();

        user.MarkMfaDisabled();

        user.MfaEnrolled.Should().BeFalse();
        user.DomainEvents.Should().ContainSingle(e => e is UserMfaDisabledEvent);
        var evt = (UserMfaDisabledEvent)user.DomainEvents[0];
        evt.UserId.Should().Be(user.Id);
        evt.ByOwnerAction.Should().BeFalse();
    }

    [Fact]
    public void MarkMfaReset_RaisesEventWithByOwnerActionTrue()
    {
        var user = User.Create("u@example.com", ValidHash, UserRole.Owner);
        user.MarkMfaEnrolled();
        user.ClearDomainEvents();

        user.MarkMfaReset();

        user.MfaEnrolled.Should().BeFalse();
        user.DomainEvents.Should().ContainSingle(e => e is UserMfaDisabledEvent);
        ((UserMfaDisabledEvent)user.DomainEvents[0]).ByOwnerAction.Should().BeTrue();
    }

    [Fact]
    public void MarkMfa_EnrollDisableRoundTrip_FiresBothEvents()
    {
        var user = User.Create("u@example.com", ValidHash, UserRole.Picker);
        user.MarkMfaEnrolled();
        user.MarkMfaDisabled();
        user.MarkMfaEnrolled();

        user.MfaEnrolled.Should().BeTrue();
        user.DomainEvents.Should().HaveCount(4); // UserCreated + Enrolled + Disabled + Enrolled
    }
}
