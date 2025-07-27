﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ray.BiliBiliTool.Agent;
using Ray.BiliBiliTool.Agent.BiliBiliAgent.Dtos;
using Ray.BiliBiliTool.Agent.BiliBiliAgent.Interfaces;
using Ray.BiliBiliTool.Config.Options;
using Ray.BiliBiliTool.DomainService.Interfaces;

namespace Ray.BiliBiliTool.DomainService;

/// <summary>
/// 充电
/// </summary>
public class ChargeDomainService(
    ILogger<ChargeDomainService> logger,
    IOptionsMonitor<DailyTaskOptions> dailyTaskOptions,
    IOptionsMonitor<ChargeTaskOptions> chargeTaskOptions,
    IDailyTaskApi dailyTaskApi,
    IChargeApi chargeApi
) : IChargeDomainService
{
    private readonly DailyTaskOptions _dailyTaskOptions = dailyTaskOptions.CurrentValue;
    private readonly ChargeTaskOptions _chargeTaskOptions = chargeTaskOptions.CurrentValue;
    private readonly IDailyTaskApi _dailyTaskApi = dailyTaskApi;

    /// <summary>
    /// 月底自动己充电
    /// 仅充会到期的B币券，低于2的时候不会充
    /// </summary>
    public async Task Charge(UserInfo userInfo, BiliCookie ck)
    {
        //大会员类型
        VipType vipType = userInfo.GetVipType();
        if (vipType != VipType.Annual)
        {
            logger.LogInformation("不是年度大会员，跳过");
            return;
        }

        logger.LogInformation("【今天】{today}号", DateTime.Today.Day);

        //B币券余额
        decimal couponBalance = userInfo.Wallet?.Coupon_balance ?? 0;
        logger.LogInformation("【B币券】{couponBalance}", couponBalance);
        if (couponBalance < 2)
        {
            logger.LogInformation("余额小于2，无法充电");
            return;
        }

        //如果没有配置或配了-1，则使用fallback值（B站最新策略已不允许为自己充电）
        string targetUpId =
            string.IsNullOrWhiteSpace(_chargeTaskOptions.AutoChargeUpId)
            || _chargeTaskOptions.AutoChargeUpId == "-1"
                ? Config.Constants.FallbackAutoChargeUpId
                : _chargeTaskOptions.AutoChargeUpId!;

        logger.LogDebug("【目标Up】{up}", targetUpId);

        var request = new ChargeRequest(couponBalance, long.Parse(targetUpId), ck.BiliJct);

        //BiliApiResponse<ChargeResponse> response = await _chargeApi.Charge(decimal.ToInt32(couponBalance * 10), _dailyTaskOptions.AutoChargeUpId, _cookieOptions.UserId, _cookieOptions.BiliJct);
        BiliApiResponse<ChargeV2Response> response = await chargeApi.ChargeV2Async(
            request,
            ck.ToString()
        );

        if (response.Code == 0)
        {
            if (response.Data?.Status == 4)
            {
                logger.LogInformation("【充电结果】成功");
                logger.LogInformation("【充值个数】 {num}个B币", couponBalance);
                logger.LogInformation("经验+{exp} √", couponBalance);
                logger.LogInformation("在过期前使用成功，赠送的B币券没有浪费哦~");

                //充电留言
                await ChargeComments(response.Data.Order_no, ck);
            }
            else
            {
                logger.LogInformation("【充电结果】失败");
                logger.LogError("【原因】{reason}", response.ToJsonStr());
            }
        }
        else
        {
            logger.LogInformation("【充电结果】失败");
            logger.LogError("【原因】{reason}", response.Message);
        }
    }

    /// <summary>
    /// 充电后留言
    /// </summary>
    /// <param name="token"></param>
    public async Task ChargeComments(string orderNum, BiliCookie ck)
    {
        var comment = _chargeTaskOptions.ChargeComment ?? "";
        var request = new ChargeCommentRequest(orderNum, comment, ck.BiliJct);
        await chargeApi.ChargeCommentAsync(request, ck.ToString());

        logger.LogInformation("【留言】{comment}", comment);
    }
}
