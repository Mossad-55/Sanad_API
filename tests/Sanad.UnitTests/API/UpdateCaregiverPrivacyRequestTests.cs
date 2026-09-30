using System.ComponentModel.DataAnnotations;
using Sanad.API.Controllers.Requests;

namespace Sanad.UnitTests.API;

public sealed class UpdateCaregiverPrivacyRequestTests
{
    [Fact]
    public void Validate_ShouldRejectMissingBooleanFields()
    {
        var parameters = typeof(UpdateCaregiverPrivacyRequest)
            .GetConstructors()
            .Single()
            .GetParameters();

        Assert.Equal(4, parameters.Length);

        foreach (var parameter in parameters)
        {
            var required = parameter.GetCustomAttributes(typeof(RequiredAttribute), inherit: false)
                .Cast<RequiredAttribute>()
                .SingleOrDefault();

            Assert.NotNull(required);
            Assert.False(required.IsValid(null));
            Assert.True(required.IsValid(false));
        }
    }
}
