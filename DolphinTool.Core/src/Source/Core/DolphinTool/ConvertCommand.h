// Copyright 2021 Dolphin Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
//
// [PATCH] g_status_callback 전역 추가 — DLL 래퍼에서 진행률 콜백 주입용

#pragma once

#include <functional>
#include <string>
#include <vector>

namespace DolphinTool
{

// DLL 래퍼에서 변환 전에 세팅, 변환 완료 후 nullptr로 초기화
// nullptr이면 기존 NOOP 동작 (dolphin-tool 단독 실행 시)
using StatusCallback = std::function<bool(const std::string&, float)>;
extern StatusCallback g_status_callback;

int ConvertCommand(const std::vector<std::string>& args);

}  // namespace DolphinTool
