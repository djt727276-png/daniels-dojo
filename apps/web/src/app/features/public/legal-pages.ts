import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

import { ProsePage } from './prose-page';

/**
 * The legal set.
 *
 * Written for the platform Daniel's Dojo actually is: courses sold directly, payments through
 * Stripe, sign-in through Microsoft Entra External ID, video through Mux, files in Azure.
 * Nothing here claims a certification, a guarantee, or a fact about people that does not
 * exist. These are honest working documents an operator can refine with counsel, not
 * boilerplate pretending to be one.
 */
@Component({
  selector: 'app-privacy-policy',
  imports: [ProsePage],
  template: `
    <app-prose-page
      title="Privacy policy"
      description="What Daniel's Dojo stores about you, why, and what control you have."
    >
      <h2>What we store</h2>
      <p>
        Your account profile (display name, email address, and the identifier issued by our sign-in
        provider), your course purchases and membership state, your lesson progress, and the content
        you post in the community — discussions, replies, and direct messages.
      </p>

      <h2>What we never store</h2>
      <p>
        Passwords — sign-in is handled entirely by Microsoft Entra External ID, and your password
        never reaches our servers. Card numbers — payment details go directly to Stripe; we keep
        only order records and Stripe's reference identifiers.
      </p>

      <h2>Why we store it</h2>
      <p>
        To operate your account, deliver the courses you hold, resume your progress, run the
        community, and meet financial record-keeping obligations. We do not sell personal data.
      </p>

      <h2>Service providers</h2>
      <ul>
        <li>Microsoft Azure — hosting, database, and file storage.</li>
        <li>Microsoft Entra External ID — account sign-up and sign-in.</li>
        <li>Mux — video processing and streaming.</li>
        <li>Stripe — payment processing.</li>
      </ul>

      <h2>Your choices</h2>
      <p>
        You can update your profile at any time, download an export of your data, or delete your
        account — both directly from the account page, no support ticket required. The export is a
        JSON file containing your account details, community profile, friendships, the messages and
        posts you wrote, your reviews, enrollments, certificates, and order summaries.
      </p>

      <h2>What deletion does, exactly</h2>
      <ul>
        <li>
          Removed immediately: your community profile and handle, your photo, friendships and friend
          requests, blocks, notifications, and your name and email from our account records.
        </li>
        <li>
          Emptied but kept in shape: direct messages you sent become "message deleted" placeholders
          so the other person's conversation still makes sense; your course reviews are withdrawn.
        </li>
        <li>
          Kept without your name: forum posts remain, shown as "Former member", because removing
          them would tear holes in other members' discussions.
        </li>
        <li>
          Kept as required records: order and payment records (typically 7 years for tax law),
          certificate issuance records so earned certificates stay verifiable, and the audit trail
          of administrative actions. None of these carry your name after deletion.
        </li>
        <li>
          Your sign-in link is severed. If you return, you start a completely new account — nothing
          from before is reattached.
        </li>
      </ul>

      <h2>Cookies and analytics</h2>
      <p>
        Daniel's Dojo uses only the storage required to keep you signed in and remember device
        preferences such as your theme. There is no third-party advertising or cross-site tracking,
        which is why there is no cookie banner.
      </p>

      <h2>Contact</h2>
      <p>Questions about this policy: use the contact page and mention "privacy".</p>
    </app-prose-page>
  `,
})
export class PrivacyPolicy {}

@Component({
  selector: 'app-terms-of-service',
  imports: [ProsePage, RouterLink],
  template: `
    <app-prose-page
      title="Terms of service"
      description="The agreement between you and Daniel's Dojo when you use the platform."
    >
      <h2>Your account</h2>
      <p>
        You are responsible for activity on your account. One person per account; keep your sign-in
        credentials to yourself.
      </p>

      <h2>Course access</h2>
      <p>
        A membership grants access to membership-included courses while it is active. A lifetime
        purchase grants ongoing access to that specific course. Access is for your personal learning
        — sharing accounts, redistributing videos, or re-selling materials is not permitted.
      </p>

      <h2>Payments</h2>
      <p>
        Prices are shown before you buy and processed by Stripe. Memberships renew until cancelled;
        cancelling keeps access until the end of the paid period. See the
        <a routerLink="/legal/refunds">refund policy</a> for refunds.
      </p>

      <h2>Community</h2>
      <p>
        The <a routerLink="/legal/community-guidelines">community guidelines</a> are part of these
        terms. Moderation may remove content or restrict accounts that break them.
      </p>

      <h2>Content ownership</h2>
      <p>
        Course materials belong to Daniel's Dojo. What you write in the community remains yours; you
        grant the platform permission to display it to the people you posted it to.
      </p>

      <h2>Service changes</h2>
      <p>
        Courses may be updated and improved over time. If the platform ever retires a course you
        purchased outright, you will retain access to the materials you bought.
      </p>

      <h2>Liability</h2>
      <p>
        Courses are educational content provided as-is. Daniel's Dojo is not liable for outcomes of
        applying what you learn beyond what applicable law requires.
      </p>
    </app-prose-page>
  `,
})
export class TermsOfService {}

@Component({
  selector: 'app-refund-policy',
  imports: [ProsePage],
  template: `
    <app-prose-page
      title="Refund policy"
      description="Simple and honest: if the platform failed you, we make it right."
    >
      <h2>Memberships</h2>
      <p>
        Cancel any time from your account page; you keep access until the end of the paid period,
        and no further charges occur. If you were charged in error — a duplicate charge, a renewal
        after cancellation — contact us and we will refund it.
      </p>

      <h2>Course purchases</h2>
      <p>
        If a purchased course does not work for you — technically or otherwise — contact us within
        14 days of purchase for a full refund, provided the course has not been substantially
        completed.
      </p>

      <h2>How refunds happen</h2>
      <p>
        Refunds go back to the original payment method through Stripe, and the associated course
        access ends when the refund is issued. Every refund is reviewed by a person.
      </p>

      <h2>Chargebacks</h2>
      <p>
        Please contact us before disputing a charge with your bank — most issues are fixed faster
        directly.
      </p>
    </app-prose-page>
  `,
})
export class RefundPolicy {}

@Component({
  selector: 'app-community-guidelines',
  imports: [ProsePage],
  template: `
    <app-prose-page
      title="Community guidelines"
      description="The dojo rules: train hard, respect the people training beside you."
    >
      <h2>Expected</h2>
      <ul>
        <li>Be constructive — critique code and ideas, not people.</li>
        <li>Search before asking; answer when you can.</li>
        <li>Keep discussions on the course topic they belong to.</li>
        <li>Credit sources when you share someone else's work.</li>
      </ul>

      <h2>Not tolerated</h2>
      <ul>
        <li>Harassment, hate, or personal attacks — anywhere, including direct messages.</li>
        <li>Spam, self-promotion unrelated to the discussion, or scraping.</li>
        <li>Sharing course materials outside the platform.</li>
        <li>Posting anyone's private information.</li>
      </ul>

      <h2>Enforcement</h2>
      <p>
        Reports are reviewed by moderators. Depending on severity, action ranges from content
        removal to account suspension. Destructive moderation always records a reason, and blocking
        someone always stops their messages and requests to you in both directions.
      </p>
    </app-prose-page>
  `,
})
export class CommunityGuidelines {}

@Component({
  selector: 'app-accessibility-statement',
  imports: [ProsePage],
  template: `
    <app-prose-page
      title="Accessibility"
      description="Daniel's Dojo is built to be usable by everyone. Here is where that stands."
    >
      <h2>What is in place</h2>
      <ul>
        <li>Full keyboard navigation, with a skip link and visible focus states.</li>
        <li>Light, dark, and system themes with WCAG-oriented contrast.</li>
        <li>Reduced-motion support that follows your operating system preference.</li>
        <li>Screen-reader landmarks, labels, and status announcements.</li>
        <li>Layouts that work from 320px-wide screens upward without horizontal scrolling.</li>
        <li>Caption track support on course video.</li>
      </ul>

      <h2>Known limits</h2>
      <p>
        Captions exist per lesson only where the author has uploaded a track; coverage grows with
        the catalog. If you hit anything unusable, we want to know.
      </p>

      <h2>Feedback</h2>
      <p>
        Use the contact page and mention "accessibility" — reports of real barriers are prioritised
        like defects, because they are.
      </p>
    </app-prose-page>
  `,
})
export class AccessibilityStatement {}
