using System.ComponentModel;
using System.Globalization;

namespace Equibles.Sec.Data.Models;

public class DocumentTypeConverter : TypeConverter {
    public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType) {
        return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    }

    public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value) {
        if (value is string stringValue) {
            return DocumentType.FromValue(stringValue)
                   ?? throw new FormatException($"Unknown DocumentType value: '{stringValue}'");
        }

        return base.ConvertFrom(context, culture, value);
    }

    public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType) {
        if (destinationType == typeof(string) && value is DocumentType documentType) {
            return documentType.Value;
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }
}
