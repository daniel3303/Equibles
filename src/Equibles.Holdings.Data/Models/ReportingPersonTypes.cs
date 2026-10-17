using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// The SEC type codes a Schedule 13D or 13G states for a reporting person (cover page) or for the
/// persons filing (13G Item 3). A person often states several, such as a parent that is also a
/// corporation (HC and CO).
/// </summary>
[Flags]
public enum ReportingPersonTypes
{
    [Display(Name = "None")]
    None = 0,

    [Display(Name = "Broker-dealer (BD)")]
    BrokerDealer = 1 << 0,

    [Display(Name = "Bank (BK)")]
    Bank = 1 << 1,

    [Display(Name = "Insurance company (IC)")]
    InsuranceCompany = 1 << 2,

    [Display(Name = "Registered investment company (IV)")]
    InvestmentCompany = 1 << 3,

    [Display(Name = "Investment adviser (IA)")]
    InvestmentAdviser = 1 << 4,

    [Display(Name = "Employee benefit plan or endowment fund (EP)")]
    EmployeeBenefitPlan = 1 << 5,

    [Display(Name = "Parent holding company or control person (HC)")]
    ParentHoldingCompany = 1 << 6,

    [Display(Name = "Savings association (SA)")]
    SavingsAssociation = 1 << 7,

    [Display(Name = "Church plan (CP)")]
    ChurchPlan = 1 << 8,

    [Display(Name = "Corporation (CO)")]
    Corporation = 1 << 9,

    [Display(Name = "Partnership (PN)")]
    Partnership = 1 << 10,

    [Display(Name = "Individual (IN)")]
    Individual = 1 << 11,

    [Display(Name = "Non-US institution (FI)")]
    NonUsInstitution = 1 << 12,

    [Display(Name = "Other (OO)")]
    Other = 1 << 13,
}
